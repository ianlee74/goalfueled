namespace GoalFueled.Domain.Nutrition;

/// <summary>
/// A measured quantity of a single nutrient, expressed in the unit defined for that nutrient
/// in the <see cref="NutrientCatalog"/>. Stored on ingredients as a flat collection so the
/// document model stays simple and new nutrients can be added without schema changes.
/// </summary>
public sealed class NutrientAmount
{
    /// <summary>Key into <see cref="NutrientCatalog"/> (e.g. "protein").</summary>
    public string NutrientId { get; set; } = string.Empty;

    /// <summary>Amount in the nutrient's canonical unit (see <see cref="NutrientDefinition.Unit"/>).</summary>
    public double Value { get; set; }

    public NutrientAmount() { }

    public NutrientAmount(string nutrientId, double value)
    {
        NutrientId = nutrientId;
        Value = value;
    }
}

/// <summary>
/// Helpers for aggregating nutrient amounts (e.g. summing the ingredients of a recipe, or
/// scaling an ingredient's per-serving values by a quantity).
/// </summary>
public static class NutritionMath
{
    /// <summary>Returns a new collection with each value multiplied by <paramref name="factor"/>.</summary>
    public static IReadOnlyList<NutrientAmount> Scale(IEnumerable<NutrientAmount> amounts, double factor) =>
        amounts.Select(a => new NutrientAmount(a.NutrientId, a.Value * factor)).ToList();

    /// <summary>Sums any number of nutrient collections, grouping by nutrient id.</summary>
    public static IReadOnlyList<NutrientAmount> Sum(IEnumerable<IEnumerable<NutrientAmount>> collections)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in collections)
        {
            foreach (var amount in collection)
            {
                totals[amount.NutrientId] = totals.GetValueOrDefault(amount.NutrientId) + amount.Value;
            }
        }

        // Preserve catalog ordering for stable, label-like output.
        return NutrientCatalog.All
            .Where(def => totals.ContainsKey(def.Id))
            .Select(def => new NutrientAmount(def.Id, totals[def.Id]))
            .ToList();
    }
}
