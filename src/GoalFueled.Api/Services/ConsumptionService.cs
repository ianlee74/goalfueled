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
    private readonly MealPlanService _plans;
    private readonly MealNutritionCalculator _mealNutrition;
    private readonly RecipeNutritionCalculator _recipeNutrition;

    public ConsumptionService(
        IConsumptionEntryRepository repo,
        IMealRepository meals,
        IRecipeRepository recipes,
        IIngredientRepository ingredients,
        MealPlanService plans)
    {
        _repo = repo;
        _meals = meals;
        _recipes = recipes;
        _plans = plans;
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

    // ---- plan-driven tasks (The Log "follows along" with the active plan) ----

    /// <summary>
    /// The active plan's planned-meal tasks for a day, each marked done when a completion entry exists.
    /// A task is "done" when a consumption entry for the same date carries its correlation
    /// <see cref="ConsumptionEntry.PlannedMealRef"/>; that entry's id is returned so the client can
    /// un-complete it. When the user has no active plan, returns an empty task list with a null plan id.
    /// </summary>
    public async Task<PlannedDayDto> GetPlannedDayAsync(AppUser user, DateOnly? date, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(user.ActiveMealPlanId))
            return new PlannedDayDto(day, null, null, Array.Empty<PlannedMealTaskDto>());

        var planId = user.ActiveMealPlanId;
        var tasks = await _plans.GetPlannedMealsForDateAsync(user, planId, day, ct);

        // Completion entries for this day, indexed by their correlation ref (most recent wins).
        var entries = await _repo.ListForUserAndDateAsync(user.Id, day, ct);
        var completionByRef = entries
            .Where(e => e.PlannedMealRef is not null)
            .GroupBy(e => e.PlannedMealRef!)
            .ToDictionary(g => g.Key, g => g.First()); // already ordered most-recent-first

        var resolved = tasks
            .Select(t => completionByRef.TryGetValue(t.Ref, out var done)
                ? t with { Done = true, CompletedEntryId = done.Id }
                : t)
            .ToList();

        // A plan with no scheduled meals at all still wants its name surfaced once chosen.
        var planName = await ResolvePlanNameAsync(user, planId, ct);
        return new PlannedDayDto(day, planId, planName, resolved);
    }

    /// <summary>
    /// Completes a planned-meal task: records a consumption entry (Source=Meal, 1 serving) tagged with the
    /// task's correlation ref so The Log shows it checked. Idempotent — if the task is already complete for
    /// the date, the existing entry is returned rather than logging a duplicate (so totals never double-count).
    /// </summary>
    public async Task<ConsumptionEntryDto> CompletePlannedMealAsync(
        AppUser user, CompletePlannedMealInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.ActiveMealPlanId))
            throw new ValidationException("No active meal plan is selected.");
        if (string.IsNullOrWhiteSpace(input.Ref))
            throw new ValidationException("A planned-meal reference is required.");

        var day = input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (day > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Consumption date cannot be in the future.");

        var task = (await _plans.GetPlannedMealsForDateAsync(user, user.ActiveMealPlanId, day, ct))
            .FirstOrDefault(t => t.Ref == input.Ref)
            ?? throw new NotFoundException("That planned meal is not part of your active plan for this day.");

        // Idempotent: reuse an existing completion for this ref+date instead of logging a duplicate.
        var existing = (await _repo.ListForUserAndDateAsync(user.Id, day, ct))
            .FirstOrDefault(e => e.PlannedMealRef == input.Ref);
        if (existing is not null) return await ToDtoAsync(existing, user, ct);

        var meal = await _meals.GetByIdAsync(task.MealId, ct)
            ?? throw new ValidationException("The planned meal was not found.");
        if (!CanUseMeal(meal, user))
            throw new ValidationException("That meal is not available to you.");

        var entry = new ConsumptionEntry
        {
            OwnerUserId = user.Id,
            Date = day,
            Source = ConsumptionSource.Meal,
            SourceId = meal.Id,
            Name = meal.Name,
            Servings = 1,
            PlannedMealRef = input.Ref,
        };
        await _repo.AddAsync(entry, ct);
        return await ToDtoAsync(entry, user, ct);
    }

    /// <summary>
    /// Un-completes a planned-meal task by removing its completion entry for the given date. No-op if the
    /// task was not complete. Identified by the task's correlation ref so callers need not track the entry id.
    /// </summary>
    public async Task UncompletePlannedMealAsync(
        AppUser user, CompletePlannedMealInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Ref)) return;
        var day = input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var matches = (await _repo.ListForUserAndDateAsync(user.Id, day, ct))
            .Where(e => e.PlannedMealRef == input.Ref)
            .ToList();
        foreach (var entry in matches)
            await _repo.DeleteAsync(entry, ct);
    }

    private async Task<string?> ResolvePlanNameAsync(AppUser user, string planId, CancellationToken ct)
    {
        try
        {
            var plan = await _plans.GetAsync(user, planId, ct);
            return plan.Name;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The caller's <em>actual</em> logged nutrition, summed per calendar day over the inclusive date range
    /// <paramref name="from"/>..<paramref name="to"/>, keyed by date. Only days that have at least one logged
    /// entry appear in the result; each day's value is the sum of every entry's computed nutrition (the same
    /// per-entry computation the day view uses), so it is on the same basis as the meal-plan roll-up.
    ///
    /// <para>Used by <see cref="GoalService"/> to let nutrient-goal progress reflect what was actually eaten,
    /// falling back to planned intake only for days the user has not logged.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<NutrientAmount>>> DailyNutritionInRangeAsync(
        AppUser user, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var entries = await _repo.ListForUserInRangeAsync(user.Id, from, to, ct);
        var byDay = new Dictionary<DateOnly, List<IReadOnlyList<NutrientAmount>>>();
        foreach (var entry in entries)
        {
            var nutrition = await ComputeNutritionAsync(entry, user, ct);
            if (!byDay.TryGetValue(entry.Date, out var list))
                byDay[entry.Date] = list = new List<IReadOnlyList<NutrientAmount>>();
            list.Add(nutrition);
        }

        return byDay.ToDictionary(
            kvp => kvp.Key,
            kvp => NutritionMath.Sum(kvp.Value));
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
        var (nutrition, sourceMissing) = await ComputeWithSourceAsync(entry, user, ct);
        return new ConsumptionEntryDto(
            entry.Id, entry.Date, entry.Source, entry.SourceId, entry.Name, entry.Servings,
            nutrition, sourceMissing, entry.CreatedAt, entry.UpdatedAt, entry.PlannedMealRef);
    }

    /// <summary>An entry's computed nutrition total (no missing-source flag), for roll-ups.</summary>
    private async Task<IReadOnlyList<NutrientAmount>> ComputeNutritionAsync(
        ConsumptionEntry entry, AppUser user, CancellationToken ct) =>
        (await ComputeWithSourceAsync(entry, user, ct)).Nutrition;

    /// <summary>
    /// Computes a logged entry's nutrition total (source per-serving nutrition × servings for catalog
    /// entries, or inline free-form nutrition × servings), plus whether a catalog source is no longer
    /// visible. A missing/invisible catalog source yields empty nutrition (it cannot be resolved).
    /// </summary>
    private async Task<(IReadOnlyList<NutrientAmount> Nutrition, bool SourceMissing)> ComputeWithSourceAsync(
        ConsumptionEntry entry, AppUser user, CancellationToken ct)
    {
        switch (entry.Source)
        {
            case ConsumptionSource.Meal:
            {
                var meal = entry.SourceId is not null ? await _meals.GetByIdAsync(entry.SourceId, ct) : null;
                if (meal is null || !CanUseMeal(meal, user))
                    return (Array.Empty<NutrientAmount>(), true);

                var recipesById = await _mealNutrition.ResolveRecipesAsync(new[] { meal }, ct);
                var ingredientsById = await _mealNutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);
                var perMeal = MealNutritionCalculator.ComputeTotal(meal, recipesById, ingredientsById);
                return (NutritionMath.Scale(perMeal, entry.Servings), false);
            }
            case ConsumptionSource.Recipe:
            {
                var recipe = entry.SourceId is not null ? await _recipes.GetByIdAsync(entry.SourceId, ct) : null;
                if (recipe is null || !CanUseRecipe(recipe, user))
                    return (Array.Empty<NutrientAmount>(), true);

                var ingredientsById = await _recipeNutrition.ResolveIngredientsAsync(new[] { recipe }, ct);
                var perServing = RecipeNutritionCalculator.ComputePerServing(recipe, ingredientsById);
                return (NutritionMath.Scale(perServing, entry.Servings), false);
            }
            default: // FreeForm: inline per-serving nutrition × servings.
                return (NutritionMath.Scale(entry.Nutrition, entry.Servings), false);
        }
    }
}
