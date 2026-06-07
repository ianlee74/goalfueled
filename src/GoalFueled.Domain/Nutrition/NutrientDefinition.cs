namespace GoalFueled.Domain.Nutrition;

/// <summary>The unit a nutrient amount is expressed in.</summary>
public enum NutrientUnit
{
    /// <summary>Kilocalories (the "Calories" line on a US label).</summary>
    Kcal = 0,
    Gram = 1,
    Milligram = 2,
    Microgram = 3,
    /// <summary>International Units (legacy; some vitamins).</summary>
    IU = 4
}

/// <summary>High-level grouping used to organize the nutrient list in the UI.</summary>
public enum NutrientCategory
{
    Energy = 0,
    Macronutrient = 1,
    Fat = 2,
    Carbohydrate = 3,
    Sterol = 4,
    Mineral = 5,
    Vitamin = 6,
    Other = 7
}

/// <summary>
/// Definition of a single nutrient that can appear on a nutrition label.
/// These are reference/catalog data (see <see cref="NutrientCatalog"/>); actual measured
/// values live on an ingredient as <see cref="NutrientAmount"/> entries keyed by <see cref="Id"/>.
/// </summary>
/// <param name="Id">Stable, machine-friendly key (e.g. "total_fat"). Never change once shipped.</param>
/// <param name="Name">Display name as it appears on a label.</param>
/// <param name="Unit">Unit the amount is expressed in.</param>
/// <param name="Category">Grouping for UI organization.</param>
/// <param name="DailyValue">
/// US FDA reference Daily Value for a 2,000 calorie diet (in <see cref="Unit"/>), used to compute %DV.
/// Null where the FDA does not define one.
/// </param>
/// <param name="IsCoreLabelItem">True for the bold/primary lines always shown on a label.</param>
public sealed record NutrientDefinition(
    string Id,
    string Name,
    NutrientUnit Unit,
    NutrientCategory Category,
    double? DailyValue = null,
    bool IsCoreLabelItem = false);
