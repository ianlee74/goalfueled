using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Maps USDA FoodData Central nutrient numbers onto this app's <see cref="NutrientCatalog"/> ids,
/// and converts USDA-reported units into each nutrient's canonical unit.
/// </summary>
/// <remarks>
/// We key on the USDA <c>nutrientNumber</c> (a stable identifier, e.g. "203" = Protein) rather than
/// the display name. Only nutrients this app actually tracks are mapped; everything else USDA returns
/// is ignored. USDA reports nutrient amounts per 100 g, which is the basis our editor uses, so the
/// values are a 1:1 transfer once the unit (G/MG/UG/KCAL/IU) is reconciled with the catalog's unit.
/// </remarks>
public static class FdcNutrientMap
{
    /// <summary>USDA nutrient number → app catalog nutrient id.</summary>
    private static readonly Dictionary<string, string> ByNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- Energy ----
        ["208"] = "calories",            // Energy (kcal)

        // ---- Fats ----
        ["204"] = "total_fat",           // Total lipid (fat)
        ["606"] = "saturated_fat",       // Fatty acids, total saturated
        ["605"] = "trans_fat",           // Fatty acids, total trans
        ["646"] = "polyunsaturated_fat", // Fatty acids, total polyunsaturated
        ["645"] = "monounsaturated_fat", // Fatty acids, total monounsaturated

        // ---- Sterols ----
        ["601"] = "cholesterol",

        // ---- Carbohydrates ----
        ["205"] = "total_carbohydrate",  // Carbohydrate, by difference
        ["291"] = "dietary_fiber",       // Fiber, total dietary
        ["269"] = "total_sugars",        // Sugars, total including NLEA
        ["539"] = "added_sugars",        // Sugars, added

        // ---- Protein ----
        ["203"] = "protein",

        // ---- Vitamins ----
        ["320"] = "vitamin_a",           // Vitamin A, RAE (µg)
        ["401"] = "vitamin_c",           // Vitamin C, total ascorbic acid (mg)
        ["328"] = "vitamin_d",           // Vitamin D (D2 + D3) (µg)
        ["323"] = "vitamin_e",           // Vitamin E (alpha-tocopherol) (mg)
        ["430"] = "vitamin_k",           // Vitamin K (phylloquinone) (µg)
        ["404"] = "thiamin",
        ["405"] = "riboflavin",
        ["406"] = "niacin",
        ["415"] = "vitamin_b6",
        ["417"] = "folate",              // Folate, total (µg)
        ["418"] = "vitamin_b12",
        ["416"] = "biotin",              // Biotin (µg) — limited coverage in USDA data
        ["410"] = "pantothenic_acid",
        ["421"] = "choline",             // Choline, total (mg)

        // ---- Minerals ----
        ["301"] = "calcium",
        ["303"] = "iron",
        ["306"] = "potassium",
        ["307"] = "sodium",
        ["305"] = "phosphorus",
        ["304"] = "magnesium",
        ["309"] = "zinc",
        ["317"] = "selenium",            // µg
        ["312"] = "copper",
        ["315"] = "manganese",
        ["314"] = "iodine",              // µg
        ["322"] = "chromium",            // µg
        ["316"] = "molybdenum",          // µg

        // ---- Other ----
        ["262"] = "caffeine",            // mg
        ["255"] = "water",               // g
    };

    /// <summary>Returns the app catalog nutrient id for a USDA nutrient number, or null if untracked.</summary>
    public static string? ToCatalogId(string? nutrientNumber) =>
        nutrientNumber is not null && ByNumber.TryGetValue(nutrientNumber, out var id) ? id : null;

    /// <summary>
    /// Converts a USDA-reported value (in <paramref name="usdaUnit"/>) into the canonical unit the app
    /// stores for <paramref name="target"/>. Returns null when the units are incompatible (so a bogus
    /// reading is dropped rather than stored at the wrong magnitude).
    /// </summary>
    public static double? ConvertValue(double value, string? usdaUnit, NutrientDefinition target)
    {
        if (ParseUnit(usdaUnit) is not { } source) return null;

        // IU values can't be meaningfully converted to a mass without a per-vitamin factor, so we only
        // accept IU when the catalog itself expects IU; otherwise the mass-based reading is preferred.
        if (source == NutrientUnit.IU || target.Unit == NutrientUnit.IU)
            return source == target.Unit ? value : (double?)null;

        if (source == NutrientUnit.Kcal || target.Unit == NutrientUnit.Kcal)
            return source == target.Unit ? value : (double?)null;

        // Mass conversion via micrograms as the common base.
        var inMicrograms = value * MassToMicrograms(source);
        return inMicrograms / MassToMicrograms(target.Unit);
    }

    private static double MassToMicrograms(NutrientUnit unit) => unit switch
    {
        NutrientUnit.Gram => 1_000_000d,
        NutrientUnit.Milligram => 1_000d,
        NutrientUnit.Microgram => 1d,
        _ => double.NaN,
    };

    private static NutrientUnit? ParseUnit(string? unit) => unit?.Trim().ToUpperInvariant() switch
    {
        "KCAL" => NutrientUnit.Kcal,
        "G" => NutrientUnit.Gram,
        "MG" => NutrientUnit.Milligram,
        "UG" or "µG" or "MCG" => NutrientUnit.Microgram,
        "IU" => NutrientUnit.IU,
        _ => null,
    };
}
