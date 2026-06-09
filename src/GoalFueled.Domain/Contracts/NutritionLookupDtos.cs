using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// Result of an automatic nutrition lookup for a free-text ingredient name. The amounts are
/// expressed per 100 g (the basis the external source — USDA FoodData Central — reports and the
/// basis our ingredient serving editor defaults to), already mapped onto this app's nutrient
/// catalog so the client can pre-fill the editor directly.
/// </summary>
public sealed record NutritionLookupResultDto(
    /// <summary>The description of the food the source matched (e.g. "Bananas, raw").</summary>
    string MatchedName,
    /// <summary>Human-readable name of the data source (e.g. "USDA FoodData Central").</summary>
    string Source,
    /// <summary>The amount of food the nutrient values describe, in grams (always 100).</summary>
    double BasisGrams,
    /// <summary>Catalog-keyed nutrient amounts, in each nutrient's canonical unit, per <see cref="BasisGrams"/>.</summary>
    IReadOnlyList<NutrientAmount> NutritionPer100g);
