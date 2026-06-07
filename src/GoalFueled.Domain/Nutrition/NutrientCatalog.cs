using System.Collections.ObjectModel;

namespace GoalFueled.Domain.Nutrition;

/// <summary>
/// The canonical set of nutrients the app understands, modeled on the US FDA "Nutrition Facts"
/// label (2016 redesign) plus a handful of commonly-tracked extras. Daily Values are the FDA
/// reference amounts for adults &amp; children 4+ on a 2,000 kcal diet.
/// </summary>
public static class NutrientCatalog
{
    /// <summary>All known nutrients, ordered roughly as they appear on a label.</summary>
    public static readonly ReadOnlyCollection<NutrientDefinition> All = new(new List<NutrientDefinition>
    {
        // ---- Energy ----
        new("calories", "Calories", NutrientUnit.Kcal, NutrientCategory.Energy, DailyValue: 2000, IsCoreLabelItem: true),

        // ---- Fats ----
        new("total_fat", "Total Fat", NutrientUnit.Gram, NutrientCategory.Fat, DailyValue: 78, IsCoreLabelItem: true),
        new("saturated_fat", "Saturated Fat", NutrientUnit.Gram, NutrientCategory.Fat, DailyValue: 20, IsCoreLabelItem: true),
        new("trans_fat", "Trans Fat", NutrientUnit.Gram, NutrientCategory.Fat, DailyValue: null, IsCoreLabelItem: true),
        new("polyunsaturated_fat", "Polyunsaturated Fat", NutrientUnit.Gram, NutrientCategory.Fat),
        new("monounsaturated_fat", "Monounsaturated Fat", NutrientUnit.Gram, NutrientCategory.Fat),

        // ---- Sterols ----
        new("cholesterol", "Cholesterol", NutrientUnit.Milligram, NutrientCategory.Sterol, DailyValue: 300, IsCoreLabelItem: true),

        // ---- Minerals (electrolytes first, as on the label) ----
        new("sodium", "Sodium", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 2300, IsCoreLabelItem: true),

        // ---- Carbohydrates ----
        new("total_carbohydrate", "Total Carbohydrate", NutrientUnit.Gram, NutrientCategory.Carbohydrate, DailyValue: 275, IsCoreLabelItem: true),
        new("dietary_fiber", "Dietary Fiber", NutrientUnit.Gram, NutrientCategory.Carbohydrate, DailyValue: 28, IsCoreLabelItem: true),
        new("total_sugars", "Total Sugars", NutrientUnit.Gram, NutrientCategory.Carbohydrate, DailyValue: null, IsCoreLabelItem: true),
        new("added_sugars", "Added Sugars", NutrientUnit.Gram, NutrientCategory.Carbohydrate, DailyValue: 50, IsCoreLabelItem: true),
        new("sugar_alcohol", "Sugar Alcohol", NutrientUnit.Gram, NutrientCategory.Carbohydrate),

        // ---- Protein ----
        new("protein", "Protein", NutrientUnit.Gram, NutrientCategory.Macronutrient, DailyValue: 50, IsCoreLabelItem: true),

        // ---- Vitamins ----
        new("vitamin_a", "Vitamin A", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 900),
        new("vitamin_c", "Vitamin C", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 90),
        new("vitamin_d", "Vitamin D", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 20, IsCoreLabelItem: true),
        new("vitamin_e", "Vitamin E", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 15),
        new("vitamin_k", "Vitamin K", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 120),
        new("thiamin", "Thiamin (B1)", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 1.2),
        new("riboflavin", "Riboflavin (B2)", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 1.3),
        new("niacin", "Niacin (B3)", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 16),
        new("vitamin_b6", "Vitamin B6", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 1.7),
        new("folate", "Folate", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 400),
        new("vitamin_b12", "Vitamin B12", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 2.4),
        new("biotin", "Biotin (B7)", NutrientUnit.Microgram, NutrientCategory.Vitamin, DailyValue: 30),
        new("pantothenic_acid", "Pantothenic Acid (B5)", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 5),
        new("choline", "Choline", NutrientUnit.Milligram, NutrientCategory.Vitamin, DailyValue: 550),

        // ---- Minerals ----
        new("calcium", "Calcium", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 1300, IsCoreLabelItem: true),
        new("iron", "Iron", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 18, IsCoreLabelItem: true),
        new("potassium", "Potassium", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 4700, IsCoreLabelItem: true),
        new("phosphorus", "Phosphorus", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 1250),
        new("magnesium", "Magnesium", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 420),
        new("zinc", "Zinc", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 11),
        new("selenium", "Selenium", NutrientUnit.Microgram, NutrientCategory.Mineral, DailyValue: 55),
        new("copper", "Copper", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 0.9),
        new("manganese", "Manganese", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 2.3),
        new("iodine", "Iodine", NutrientUnit.Microgram, NutrientCategory.Mineral, DailyValue: 150),
        new("chromium", "Chromium", NutrientUnit.Microgram, NutrientCategory.Mineral, DailyValue: 35),
        new("molybdenum", "Molybdenum", NutrientUnit.Microgram, NutrientCategory.Mineral, DailyValue: 45),
        new("chloride", "Chloride", NutrientUnit.Milligram, NutrientCategory.Mineral, DailyValue: 2300),

        // ---- Other commonly tracked ----
        new("caffeine", "Caffeine", NutrientUnit.Milligram, NutrientCategory.Other),
        new("water", "Water", NutrientUnit.Gram, NutrientCategory.Other),
    });

    private static readonly Dictionary<string, NutrientDefinition> ById =
        All.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the nutrient with the given id, or null if it is not a known nutrient.</summary>
    public static NutrientDefinition? Find(string id) =>
        ById.TryGetValue(id, out var def) ? def : null;

    /// <summary>True if <paramref name="id"/> is a recognized nutrient key.</summary>
    public static bool IsKnown(string id) => ById.ContainsKey(id);
}
