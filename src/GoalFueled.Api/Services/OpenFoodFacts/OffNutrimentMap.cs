using System.Text.Json;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Maps Open Food Facts nutriment ids onto this app's <see cref="NutrientCatalog"/> and reconciles units.
/// </summary>
/// <remarks>
/// OFF exposes each nutriment under a base id (e.g. "proteins", "energy-kcal", "sodium") with suffixes:
/// <c>_100g</c> (per 100 g), <c>_serving</c> (per labelled serving), and <c>_unit</c>. The <c>_100g</c>
/// values are normalized to a canonical unit: <b>kilocalories</b> for energy and <b>grams</b> for every
/// mass nutriment — including vitamins and minerals (e.g. sodium 0.5, calcium 0.12, iron 0.0008, all in
/// grams). So a read is a straight grams→catalog-unit conversion, and a write is the inverse.
/// Only nutriments this app tracks are mapped; everything else OFF returns is ignored.
/// </remarks>
public static class OffNutrimentMap
{
    /// <summary>App catalog nutrient id → OFF base nutriment id.</summary>
    private static readonly Dictionary<string, string> ToOffKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calories"] = "energy-kcal",
        ["total_fat"] = "fat",
        ["saturated_fat"] = "saturated-fat",
        ["trans_fat"] = "trans-fat",
        ["polyunsaturated_fat"] = "polyunsaturated-fat",
        ["monounsaturated_fat"] = "monounsaturated-fat",
        ["cholesterol"] = "cholesterol",
        ["sodium"] = "sodium",
        ["total_carbohydrate"] = "carbohydrates",
        ["dietary_fiber"] = "fiber",
        ["total_sugars"] = "sugars",
        ["added_sugars"] = "added-sugars",
        ["protein"] = "proteins",
        ["vitamin_a"] = "vitamin-a",
        ["vitamin_c"] = "vitamin-c",
        ["vitamin_d"] = "vitamin-d",
        ["vitamin_e"] = "vitamin-e",
        ["vitamin_k"] = "vitamin-k",
        ["thiamin"] = "vitamin-b1",
        ["riboflavin"] = "vitamin-b2",
        ["niacin"] = "vitamin-pp",
        ["vitamin_b6"] = "vitamin-b6",
        ["folate"] = "vitamin-b9",
        ["vitamin_b12"] = "vitamin-b12",
        ["biotin"] = "biotin",
        ["pantothenic_acid"] = "pantothenic-acid",
        ["calcium"] = "calcium",
        ["iron"] = "iron",
        ["potassium"] = "potassium",
        ["phosphorus"] = "phosphorus",
        ["magnesium"] = "magnesium",
        ["zinc"] = "zinc",
        ["selenium"] = "selenium",
        ["copper"] = "copper",
        ["manganese"] = "manganese",
        ["iodine"] = "iodine",
        ["caffeine"] = "caffeine",
    };

    /// <summary>Returns the OFF base nutriment id for a catalog nutrient, or null if untracked.</summary>
    public static string? ToOffBaseKey(string catalogId) =>
        ToOffKey.TryGetValue(catalogId, out var key) ? key : null;

    /// <summary>
    /// Hard physical ceiling for a mass nutrient per 100 g of food. 100 g of anything contains at most
    /// 100 g of a component; the small headroom absorbs OFF's per-serving→per-100 g rounding artifacts
    /// (e.g. pure sugar normalizing to 100.2).
    /// </summary>
    private const double MaxGramsPer100g = 100.5;

    /// <summary>
    /// Hard physical ceiling for energy per 100 g: pure fat is 9 kcal/g → 900 kcal/100 g. Anything above
    /// is a data error.
    /// </summary>
    private const double MaxKcalPer100g = 900;

    /// <summary>
    /// Projects an OFF nutriments object onto catalog-keyed amounts per 100 g, converting each value from
    /// OFF's canonical unit (kcal for energy, grams otherwise) to the nutrient's catalog unit, dropping
    /// untracked nutriments, and preserving catalog order for stable, label-like output. Sodium falls back
    /// to salt ÷ 2.5 when only salt is reported.
    /// </summary>
    /// <remarks>
    /// OFF is community data, and records with impossible values exist (e.g. 144 g of carbohydrate per
    /// 100 g, from per-bag values entered against a per-serving basis). One impossible value means the
    /// whole record can't be trusted — the plausible-looking siblings usually share the same mistake — so
    /// this returns an <b>empty</b> list rather than importing any of it. The scan flow then falls back to
    /// the label photo / manual entry.
    /// </remarks>
    public static IReadOnlyList<NutrientAmount> MapPer100g(IReadOnlyDictionary<string, JsonElement> nutriments)
    {
        var byId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in NutrientCatalog.All)
        {
            var offKey = ToOffBaseKey(def.Id);
            if (offKey is null) continue;

            var grams = ReadNumber(nutriments, $"{offKey}_100g");

            // Sodium: OFF often carries salt but not sodium — derive sodium = salt / 2.5.
            if (grams is null && def.Id == "sodium")
            {
                var salt = ReadNumber(nutriments, "salt_100g");
                if (salt is not null) grams = salt.Value / 2.5;
            }

            if (grams is null || grams < 0) continue;

            // A physically impossible value proves the record is bad — import nothing from it.
            var impossible = def.Unit == NutrientUnit.Kcal
                ? grams.Value > MaxKcalPer100g
                : grams.Value > MaxGramsPer100g;
            if (impossible) return Array.Empty<NutrientAmount>();

            var converted = FromOffCanonical(grams.Value, def);
            if (converted is null) continue;
            byId[def.Id] = converted.Value;
        }

        return NutrientCatalog.All
            .Where(def => byId.ContainsKey(def.Id))
            .Select(def => new NutrientAmount(def.Id, byId[def.Id]))
            .ToList();
    }

    /// <summary>
    /// Converts a value expressed in OFF's canonical unit (kcal for energy, grams for everything else)
    /// into the nutrient's catalog unit. Returns null for a catalog nutrient stored in a unit we can't
    /// reconcile with grams (e.g. IU vitamins), so a bogus magnitude is dropped rather than stored.
    /// </summary>
    public static double? FromOffCanonical(double offValue, NutrientDefinition target)
    {
        if (target.Unit == NutrientUnit.Kcal)
            return offValue; // OFF energy-kcal is already kcal.
        if (target.Unit == NutrientUnit.IU)
            return null;      // OFF reports grams; IU needs a per-vitamin factor we don't have.

        var inMicrograms = offValue * 1_000_000d; // grams → µg
        return inMicrograms / MassToMicrograms(target.Unit);
    }

    /// <summary>
    /// Inverse of <see cref="FromOffCanonical"/>: converts a catalog value into OFF's canonical unit
    /// (kcal for energy, grams otherwise) for a write. Returns null for units we can't reconcile.
    /// </summary>
    public static double? ToOffCanonical(double catalogValue, NutrientDefinition source)
    {
        if (source.Unit == NutrientUnit.Kcal) return catalogValue;
        if (source.Unit == NutrientUnit.IU) return null;

        var inMicrograms = catalogValue * MassToMicrograms(source.Unit);
        return inMicrograms / 1_000_000d; // µg → grams
    }

    private static double MassToMicrograms(NutrientUnit unit) => unit switch
    {
        NutrientUnit.Gram => 1_000_000d,
        NutrientUnit.Milligram => 1_000d,
        NutrientUnit.Microgram => 1d,
        _ => double.NaN,
    };

    /// <summary>
    /// Reads a nutriment value as a double. OFF values arrive as JSON numbers or numeric strings; both
    /// are accepted. Returns null when the key is missing or unparseable.
    /// </summary>
    private static double? ReadNumber(IReadOnlyDictionary<string, JsonElement> nutriments, string key)
    {
        if (!nutriments.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var n) => n,
            JsonValueKind.String when double.TryParse(
                el.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }
}
