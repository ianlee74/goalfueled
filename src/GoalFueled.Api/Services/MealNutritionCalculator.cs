using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Rolls up a meal's nutrition from its recipe portions: the sum over portions of
/// (portion servings × the recipe's per-serving nutrition). Shared by <see cref="MealService"/>
/// (which reports per-meal nutrition) and <see cref="MealPlanService"/> (which needs each scheduled
/// meal's total nutrition). Needs <see cref="IRecipeRepository"/> to resolve portioned recipes and a
/// <see cref="RecipeNutritionCalculator"/> (over <see cref="IIngredientRepository"/>) to roll up each
/// recipe's per-serving nutrition.
/// </summary>
public sealed class MealNutritionCalculator
{
    private readonly IRecipeRepository _recipes;
    private readonly RecipeNutritionCalculator _recipeNutrition;

    public MealNutritionCalculator(IRecipeRepository recipes, IIngredientRepository ingredients)
    {
        _recipes = recipes;
        _recipeNutrition = new RecipeNutritionCalculator(ingredients);
    }

    /// <summary>Loads every recipe referenced by the given meals, keyed by id (deduped).</summary>
    public async Task<Dictionary<string, Recipe>> ResolveRecipesAsync(
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

    /// <summary>Resolves the ingredients referenced by a set of recipes (delegates to the recipe calculator).</summary>
    public Task<Dictionary<string, Ingredient>> ResolveIngredientsAsync(
        IReadOnlyCollection<Recipe> recipes, CancellationToken ct) =>
        _recipeNutrition.ResolveIngredientsAsync(recipes, ct);

    /// <summary>
    /// Sums each portion's nutrition contribution: scale the referenced recipe's per-serving nutrition
    /// (computed from its ingredients via <see cref="RecipeNutritionCalculator"/>) by the portion's
    /// serving count. Portions whose recipe is missing are skipped gracefully.
    /// </summary>
    public static IReadOnlyList<NutrientAmount> ComputeTotal(
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

    /// <summary>
    /// Per-serving nutrition for a meal (total / Servings), against resolved lookups. A null or
    /// non-positive serving count is treated as 1 serving so old meals (which predate the field) and
    /// single-serving meals report per-serving == total.
    /// </summary>
    public static IReadOnlyList<NutrientAmount> ComputePerServing(
        Meal meal,
        IReadOnlyDictionary<string, Recipe> recipesById,
        IReadOnlyDictionary<string, Ingredient> ingredientsById)
    {
        var total = ComputeTotal(meal, recipesById, ingredientsById);
        var servings = meal.Servings ?? 1;
        return servings > 0 ? NutritionMath.Scale(total, 1.0 / servings) : total;
    }
}
