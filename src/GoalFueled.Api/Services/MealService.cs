using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for meals: visibility, validation, the app-vs-user ownership rules, and the
/// per-meal nutrition summary. A meal is a collection of portions of prepared recipes; its nutrition
/// is the sum over portions of (portion servings × the recipe's per-serving nutrition). All
/// authorization is enforced here so the Function endpoints stay thin. Needs
/// <see cref="IRecipeRepository"/> to resolve portioned recipes and
/// <see cref="RecipeNutritionCalculator"/> (over <see cref="IIngredientRepository"/>) to roll up each
/// recipe's per-serving nutrition.
/// </summary>
public sealed class MealService
{
    private readonly IMealRepository _repo;
    private readonly IRecipeRepository _recipes;
    private readonly RecipeNutritionCalculator _nutrition;

    public MealService(IMealRepository repo, IRecipeRepository recipes, IIngredientRepository ingredients)
    {
        _repo = repo;
        _recipes = recipes;
        _nutrition = new RecipeNutritionCalculator(ingredients);
    }

    public async Task<IReadOnlyList<MealDto>> ListAsync(AppUser user, string? search, CancellationToken ct)
    {
        var meals = await _repo.ListVisibleAsync(user.Id, search, ct);
        return await ToDtosAsync(meals, user, ct);
    }

    public async Task<MealDto> GetAsync(AppUser user, string id, CancellationToken ct)
    {
        var meal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal not found.");
        EnsureCanView(meal, user);
        return await ToDtoAsync(meal, user, ct);
    }

    public async Task<MealDto> CreateAsync(AppUser user, MealInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);

        var appLevel = input.AppLevel && user.IsActingAsAdmin;
        var meal = new Meal
        {
            Name = input.Name.Trim(),
            Portions = NormalizePortions(input.Portions),
            Scope = appLevel ? OwnershipScope.App : OwnershipScope.User,
            OwnerUserId = appLevel ? null : user.Id,
        };

        await _repo.AddAsync(meal, ct);
        return await ToDtoAsync(meal, user, ct);
    }

    public async Task<MealDto> UpdateAsync(AppUser user, string id, MealInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);
        var meal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal not found.");
        EnsureCanEdit(meal, user);

        meal.Name = input.Name.Trim();
        meal.Portions = NormalizePortions(input.Portions);
        meal.UpdatedAt = DateTimeOffset.UtcNow;

        // Admins may move a meal to/from app level via the AppLevel flag.
        if (user.IsActingAsAdmin)
        {
            meal.Scope = input.AppLevel ? OwnershipScope.App : OwnershipScope.User;
            meal.OwnerUserId = input.AppLevel ? null : (meal.OwnerUserId ?? user.Id);
        }

        await _repo.UpdateAsync(meal, ct);
        return await ToDtoAsync(meal, user, ct);
    }

    public async Task DeleteAsync(AppUser user, string id, CancellationToken ct)
    {
        var meal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal not found.");
        EnsureCanEdit(meal, user);
        await _repo.DeleteAsync(meal, ct);
    }

    // ---- authorization ----

    private static void EnsureCanView(Meal m, AppUser user)
    {
        if (m.Scope == OwnershipScope.App) return;
        if (m.OwnerUserId == user.Id) return;
        if (user.IsActingAsAdmin) return; // admins can view all data
        throw new NotFoundException("Meal not found.");
    }

    private static void EnsureCanEdit(Meal m, AppUser user)
    {
        if (user.IsActingAsAdmin) return; // admins can modify all data
        if (m.Scope == OwnershipScope.User && m.OwnerUserId == user.Id) return;
        throw new ForbiddenException("You can only modify your own meals.");
    }

    private static bool CanEdit(Meal m, AppUser user) =>
        user.IsActingAsAdmin || (m.Scope == OwnershipScope.User && m.OwnerUserId == user.Id);

    // ---- validation ----

    private async Task ValidateAsync(MealInput input, AppUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ValidationException("Name is required.");

        foreach (var portion in input.Portions ?? Array.Empty<MealPortionInput>())
        {
            if (string.IsNullOrWhiteSpace(portion.RecipeId))
                throw new ValidationException("Each meal portion must reference a recipe.");
            if (portion.Servings <= 0)
                throw new ValidationException("Each meal portion must include more than zero servings.");

            var recipe = await _recipes.GetByIdAsync(portion.RecipeId, ct)
                ?? throw new ValidationException($"Recipe '{portion.RecipeId}' was not found.");
            // The referenced recipe must be one the caller can actually see.
            if (!CanUseRecipe(recipe, user))
                throw new ValidationException($"Recipe '{recipe.Name}' is not available to you.");
        }
    }

    private static bool CanUseRecipe(Recipe r, AppUser user) =>
        r.Scope == OwnershipScope.App ||
        r.Visibility == RecipeVisibility.Public ||
        r.OwnerUserId == user.Id ||
        user.IsActingAsAdmin;

    /// <summary>Drops empty references and keeps only the persisted portion fields.</summary>
    private static List<MealPortion> NormalizePortions(IReadOnlyList<MealPortionInput>? portions)
    {
        if (portions is null || portions.Count == 0) return new();
        return portions
            .Where(p => !string.IsNullOrWhiteSpace(p.RecipeId))
            .Select(p => new MealPortion
            {
                RecipeId = p.RecipeId,
                Servings = p.Servings,
            })
            .ToList();
    }

    // ---- nutrition roll-up & mapping ----

    /// <summary>
    /// Sums each portion's nutrition contribution: scale the referenced recipe's per-serving nutrition
    /// (computed from its ingredients via <see cref="RecipeNutritionCalculator"/>) by the portion's
    /// serving count. Portions whose recipe is missing are skipped gracefully.
    /// </summary>
    private static IReadOnlyList<NutrientAmount> ComputeTotalNutrition(
        Meal meal,
        IReadOnlyDictionary<string, Recipe> recipesById,
        IReadOnlyDictionary<string, Ingredient> ingredientsById)
    {
        var contributions = new List<IEnumerable<NutrientAmount>>();
        foreach (var portion in meal.Portions)
        {
            if (!recipesById.TryGetValue(portion.RecipeId, out var recipe)) continue;
            var perServing = RecipeNutritionCalculator.ComputePerServing(recipe, ingredientsById);
            contributions.Add(NutritionMath.Scale(perServing, portion.Servings));
        }

        return NutritionMath.Sum(contributions);
    }

    private async Task<IReadOnlyList<MealDto>> ToDtosAsync(
        IReadOnlyList<Meal> meals, AppUser user, CancellationToken ct)
    {
        var recipesById = await ResolveRecipesAsync(meals, ct);
        var ingredientsById = await _nutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);
        return meals.Select(m => ToDto(m, user, recipesById, ingredientsById)).ToList();
    }

    private async Task<MealDto> ToDtoAsync(Meal meal, AppUser user, CancellationToken ct)
    {
        var recipesById = await ResolveRecipesAsync(new[] { meal }, ct);
        var ingredientsById = await _nutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);
        return ToDto(meal, user, recipesById, ingredientsById);
    }

    /// <summary>Loads every recipe referenced by the given meals, keyed by id (deduped).</summary>
    private async Task<Dictionary<string, Recipe>> ResolveRecipesAsync(
        IReadOnlyCollection<Meal> meals, CancellationToken ct)
    {
        var ids = meals.SelectMany(m => m.Portions).Select(p => p.RecipeId).Distinct();
        var byId = new Dictionary<string, Recipe>();
        foreach (var id in ids)
        {
            var recipe = await _recipes.GetByIdAsync(id, ct);
            if (recipe is not null) byId[id] = recipe;
        }
        return byId;
    }

    private static MealDto ToDto(
        Meal m, AppUser user,
        IReadOnlyDictionary<string, Recipe> recipesById,
        IReadOnlyDictionary<string, Ingredient> ingredientsById)
    {
        var total = ComputeTotalNutrition(m, recipesById, ingredientsById);
        var portions = m.Portions
            .Select(p => new MealPortionDto(
                p.RecipeId, p.Servings,
                recipesById.TryGetValue(p.RecipeId, out var r) ? r.Name : null))
            .ToList();

        return new MealDto(
            m.Id, m.Name, portions, m.Scope, m.OwnerUserId,
            total, m.CreatedAt, m.UpdatedAt, CanEdit(m, user));
    }
}
