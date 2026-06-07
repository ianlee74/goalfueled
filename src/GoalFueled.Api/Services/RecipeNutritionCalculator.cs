using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Rolls up a recipe's nutrition from its ingredient lines, resolving each referenced ingredient
/// and converting quantities into "number of servings" of that ingredient. Shared by
/// <see cref="RecipeService"/> (which reports per-recipe nutrition) and <see cref="MealService"/>
/// (which needs each portioned recipe's per-serving nutrition). Needs
/// <see cref="IIngredientRepository"/> to resolve referenced ingredients.
/// </summary>
public sealed class RecipeNutritionCalculator
{
    private readonly IIngredientRepository _ingredients;

    public RecipeNutritionCalculator(IIngredientRepository ingredients) => _ingredients = ingredients;

    /// <summary>Loads every ingredient referenced by the given recipes, keyed by id (deduped).</summary>
    public async Task<Dictionary<string, Ingredient>> ResolveIngredientsAsync(
        IReadOnlyCollection<Recipe> recipes, CancellationToken ct)
    {
        var ids = recipes.SelectMany(r => r.Items).Select(i => i.IngredientId).Distinct();
        var byId = new Dictionary<string, Ingredient>();
        foreach (var id in ids)
        {
            var ingredient = await _ingredients.GetByIdAsync(id, ct);
            if (ingredient is not null) byId[id] = ingredient;
        }
        return byId;
    }

    /// <summary>
    /// Sums each item's nutrition contribution against a pre-resolved ingredient lookup. For an item,
    /// if its unit is dimension-compatible with the referenced ingredient's serving unit, convert the
    /// item quantity into "number of servings" of that ingredient (using each unit's base-conversion
    /// factor), scale the ingredient's per-serving nutrition by that count, and add it in. Items whose
    /// ingredient is missing or whose unit is incompatible are skipped gracefully.
    /// </summary>
    public static IReadOnlyList<NutrientAmount> ComputeTotal(
        Recipe recipe, IReadOnlyDictionary<string, Ingredient> byId)
    {
        var contributions = new List<IEnumerable<NutrientAmount>>();
        foreach (var item in recipe.Items)
        {
            if (!byId.TryGetValue(item.IngredientId, out var ingredient)) continue;
            if (ingredient.ServingSize <= 0) continue;
            if (!Units.AreCompatible(item.Unit, ingredient.Unit)) continue;

            // servings = (qty in base units) / (serving size in base units).
            var itemBase = item.Quantity * Units.Get(item.Unit).ToBaseFactor;
            var servingBase = ingredient.ServingSize * Units.Get(ingredient.Unit).ToBaseFactor;
            if (servingBase <= 0) continue;
            var servings = itemBase / servingBase;

            contributions.Add(NutritionMath.Scale(ingredient.NutritionPerServing, servings));
        }

        return NutritionMath.Sum(contributions);
    }

    /// <summary>Per-serving nutrition for a recipe (total / Servings), against a resolved lookup.</summary>
    public static IReadOnlyList<NutrientAmount> ComputePerServing(
        Recipe recipe, IReadOnlyDictionary<string, Ingredient> byId)
    {
        var total = ComputeTotal(recipe, byId);
        return recipe.Servings > 0 ? NutritionMath.Scale(total, 1.0 / recipe.Servings) : total;
    }
}
