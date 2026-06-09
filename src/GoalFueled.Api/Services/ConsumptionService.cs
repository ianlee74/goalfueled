using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for the daily consumption log — what a user <em>actually</em> ate, as opposed to
/// what a meal plan <em>plans</em>. Consumption is strictly per-user data: every entry is owned by, and
/// only visible to, the logging user (no app-level or cross-user view, even for admins, mirroring
/// <see cref="WeightService"/> / <see cref="GoalService"/>).
///
/// <para>An entry either references the catalog (a <see cref="Meal"/> or <see cref="Recipe"/>), in which
/// case nutrition is computed live from the source via the same calculators the Meals/Recipes features
/// use (scaled by the logged servings), or it is a free-form item carrying its own optional per-serving
/// nutrition inline. The catalog name is snapshotted at log time so the log reads sensibly even if the
/// source is later renamed or deleted.</para>
/// </summary>
public sealed class ConsumptionService
{
    private readonly IConsumptionEntryRepository _repo;
    private readonly IMealRepository _meals;
    private readonly IRecipeRepository _recipes;
    private readonly MealNutritionCalculator _mealNutrition;
    private readonly RecipeNutritionCalculator _recipeNutrition;

    public ConsumptionService(
        IConsumptionEntryRepository repo,
        IMealRepository meals,
        IRecipeRepository recipes,
        IIngredientRepository ingredients)
    {
        _repo = repo;
        _meals = meals;
        _recipes = recipes;
        _mealNutrition = new MealNutritionCalculator(recipes, ingredients);
        _recipeNutrition = new RecipeNutritionCalculator(ingredients);
    }

    /// <summary>The caller's consumption log for a day (defaulting to today) with per-entry and day totals.</summary>
    public async Task<ConsumptionDayDto> GetDayAsync(AppUser user, DateOnly? date, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = await _repo.ListForUserAndDateAsync(user.Id, day, ct);

        var dtos = new List<ConsumptionEntryDto>(entries.Count);
        foreach (var entry in entries)
            dtos.Add(await ToDtoAsync(entry, user, ct));

        var total = NutritionMath.Sum(dtos.Select(d => d.Nutrition));
        return new ConsumptionDayDto(day, dtos, total);
    }

    /// <summary>Logs a consumed item for the caller (a new entry is always appended — no per-day upsert).</summary>
    public async Task<ConsumptionEntryDto> LogAsync(AppUser user, ConsumptionEntryInput input, CancellationToken ct)
    {
        Validate(input);
        var date = input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var servings = input.Servings;

        var entry = new ConsumptionEntry
        {
            OwnerUserId = user.Id,
            Date = date,
            Source = input.Source,
            Servings = servings,
        };

        switch (input.Source)
        {
            case ConsumptionSource.Meal:
            {
                var meal = await _meals.GetByIdAsync(input.SourceId!, ct)
                    ?? throw new ValidationException("The selected meal was not found.");
                if (!CanUseMeal(meal, user))
                    throw new ValidationException("That meal is not available to you.");
                entry.SourceId = meal.Id;
                entry.Name = string.IsNullOrWhiteSpace(input.Name) ? meal.Name : input.Name!.Trim();
                break;
            }
            case ConsumptionSource.Recipe:
            {
                var recipe = await _recipes.GetByIdAsync(input.SourceId!, ct)
                    ?? throw new ValidationException("The selected recipe was not found.");
                if (!CanUseRecipe(recipe, user))
                    throw new ValidationException("That recipe is not available to you.");
                entry.SourceId = recipe.Id;
                entry.Name = string.IsNullOrWhiteSpace(input.Name) ? recipe.Name : input.Name!.Trim();
                break;
            }
            default: // FreeForm
                entry.SourceId = null;
                entry.Name = input.Name!.Trim();
                // Inline per-serving nutrition: keep only known nutrients, in catalog order.
                entry.Nutrition = NormalizeNutrition(input.Nutrition);
                break;
        }

        await _repo.AddAsync(entry, ct);
        return await ToDtoAsync(entry, user, ct);
    }

    /// <summary>Removes one of the caller's logged entries.</summary>
    public async Task RemoveAsync(AppUser user, string id, CancellationToken ct)
    {
        var entry = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Consumption entry not found.");
        // Entries are entirely private; not even admins get a cross-user view.
        if (entry.OwnerUserId != user.Id) throw new NotFoundException("Consumption entry not found.");
        await _repo.DeleteAsync(entry, ct);
    }

    // ---- validation ----

    private static void Validate(ConsumptionEntryInput input)
    {
        if (!Enum.IsDefined(typeof(ConsumptionSource), input.Source))
            throw new ValidationException("Unknown consumption source.");

        if (!(input.Servings > 0))
            throw new ValidationException("Servings must be greater than zero.");
        if (input.Servings > InputLimits.ConsumptionServingsMax)
            throw new ValidationException($"Servings must be {InputLimits.ConsumptionServingsMax} or less.");

        if (input.Date is { } date && date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Consumption date cannot be in the future.");

        switch (input.Source)
        {
            case ConsumptionSource.Meal:
            case ConsumptionSource.Recipe:
                if (string.IsNullOrWhiteSpace(input.SourceId))
                    throw new ValidationException("A meal or recipe entry must reference a catalog item.");
                InputLimits.LimitText(input.Name, InputLimits.NameMaxLength, "Name");
                break;
            default: // FreeForm
                InputLimits.RequireText(input.Name, InputLimits.NameMaxLength, "Name");
                InputLimits.LimitCount(input.Nutrition?.Count ?? 0, InputLimits.NutritionEntriesMax, "nutrition entries");
                break;
        }
    }

    /// <summary>Drops unknown nutrients and any non-positive values, preserving catalog order.</summary>
    private static List<NutrientAmount> NormalizeNutrition(IReadOnlyList<NutrientAmount>? nutrition)
    {
        if (nutrition is null || nutrition.Count == 0) return new();
        var valid = nutrition.Where(n => NutrientCatalog.IsKnown(n.NutrientId) && n.Value > 0);
        return NutritionMath.Sum(new[] { valid }).ToList();
    }

    // ---- catalog visibility (mirrors MealService / RecipeService) ----

    private static bool CanUseMeal(Meal m, AppUser user) =>
        m.Scope == OwnershipScope.App || m.OwnerUserId == user.Id || user.IsActingAsAdmin;

    private static bool CanUseRecipe(Recipe r, AppUser user) =>
        r.Scope == OwnershipScope.App ||
        r.Visibility == RecipeVisibility.Public ||
        r.OwnerUserId == user.Id ||
        user.IsActingAsAdmin;

    // ---- nutrition roll-up & mapping ----
    // Reuses the same calculators the Meals/Recipes features use, scaled by the logged servings, so a
    // logged item's nutrition matches what those screens show.

    private async Task<ConsumptionEntryDto> ToDtoAsync(ConsumptionEntry entry, AppUser user, CancellationToken ct)
    {
        IReadOnlyList<NutrientAmount> nutrition;
        var sourceMissing = false;

        switch (entry.Source)
        {
            case ConsumptionSource.Meal:
            {
                var meal = entry.SourceId is not null ? await _meals.GetByIdAsync(entry.SourceId, ct) : null;
                if (meal is null || !CanUseMeal(meal, user))
                {
                    nutrition = Array.Empty<NutrientAmount>();
                    sourceMissing = true;
                }
                else
                {
                    var recipesById = await _mealNutrition.ResolveRecipesAsync(new[] { meal }, ct);
                    var ingredientsById = await _mealNutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);
                    var perMeal = MealNutritionCalculator.ComputeTotal(meal, recipesById, ingredientsById);
                    nutrition = NutritionMath.Scale(perMeal, entry.Servings);
                }
                break;
            }
            case ConsumptionSource.Recipe:
            {
                var recipe = entry.SourceId is not null ? await _recipes.GetByIdAsync(entry.SourceId, ct) : null;
                if (recipe is null || !CanUseRecipe(recipe, user))
                {
                    nutrition = Array.Empty<NutrientAmount>();
                    sourceMissing = true;
                }
                else
                {
                    var ingredientsById = await _recipeNutrition.ResolveIngredientsAsync(new[] { recipe }, ct);
                    var perServing = RecipeNutritionCalculator.ComputePerServing(recipe, ingredientsById);
                    nutrition = NutritionMath.Scale(perServing, entry.Servings);
                }
                break;
            }
            default: // FreeForm: inline per-serving nutrition × servings.
                nutrition = NutritionMath.Scale(entry.Nutrition, entry.Servings);
                break;
        }

        return new ConsumptionEntryDto(
            entry.Id, entry.Date, entry.Source, entry.SourceId, entry.Name, entry.Servings,
            nutrition, sourceMissing, entry.CreatedAt, entry.UpdatedAt);
    }
}
