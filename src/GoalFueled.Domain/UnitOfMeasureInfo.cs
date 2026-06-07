namespace GoalFueled.Domain;

/// <summary>Metadata for a <see cref="UnitOfMeasure"/>: its dimension, label, and conversion to a base unit.</summary>
/// <param name="Unit">The unit being described.</param>
/// <param name="Dimension">Whether it measures mass, volume, or count.</param>
/// <param name="Abbreviation">Short label for UI (e.g. "g", "tbsp").</param>
/// <param name="ToBaseFactor">
/// Multiplier converting one of this unit into the dimension's base unit
/// (grams for mass, milliliters for volume, pieces for count).
/// </param>
public sealed record UnitOfMeasureInfo(
    UnitOfMeasure Unit,
    UnitDimension Dimension,
    string Abbreviation,
    double ToBaseFactor);

/// <summary>Lookup and conversion helpers for <see cref="UnitOfMeasure"/>.</summary>
public static class Units
{
    private static readonly Dictionary<UnitOfMeasure, UnitOfMeasureInfo> Info = new()
    {
        // Mass (base: gram)
        [UnitOfMeasure.Gram] = new(UnitOfMeasure.Gram, UnitDimension.Mass, "g", 1),
        [UnitOfMeasure.Milligram] = new(UnitOfMeasure.Milligram, UnitDimension.Mass, "mg", 0.001),
        [UnitOfMeasure.Kilogram] = new(UnitOfMeasure.Kilogram, UnitDimension.Mass, "kg", 1000),
        [UnitOfMeasure.Ounce] = new(UnitOfMeasure.Ounce, UnitDimension.Mass, "oz", 28.3495),
        [UnitOfMeasure.Pound] = new(UnitOfMeasure.Pound, UnitDimension.Mass, "lb", 453.592),

        // Volume (base: milliliter)
        [UnitOfMeasure.Milliliter] = new(UnitOfMeasure.Milliliter, UnitDimension.Volume, "ml", 1),
        [UnitOfMeasure.Liter] = new(UnitOfMeasure.Liter, UnitDimension.Volume, "l", 1000),
        [UnitOfMeasure.Teaspoon] = new(UnitOfMeasure.Teaspoon, UnitDimension.Volume, "tsp", 4.92892),
        [UnitOfMeasure.Tablespoon] = new(UnitOfMeasure.Tablespoon, UnitDimension.Volume, "tbsp", 14.7868),
        [UnitOfMeasure.FluidOunce] = new(UnitOfMeasure.FluidOunce, UnitDimension.Volume, "fl oz", 29.5735),
        [UnitOfMeasure.Cup] = new(UnitOfMeasure.Cup, UnitDimension.Volume, "cup", 236.588),

        // Count (base: piece)
        [UnitOfMeasure.Piece] = new(UnitOfMeasure.Piece, UnitDimension.Count, "pc", 1),
        [UnitOfMeasure.Serving] = new(UnitOfMeasure.Serving, UnitDimension.Count, "serving", 1),
    };

    public static UnitOfMeasureInfo Get(UnitOfMeasure unit) => Info[unit];

    public static IEnumerable<UnitOfMeasureInfo> All => Info.Values;

    /// <summary>True if two units measure the same dimension and can be converted between.</summary>
    public static bool AreCompatible(UnitOfMeasure a, UnitOfMeasure b) =>
        Info[a].Dimension == Info[b].Dimension;
}
