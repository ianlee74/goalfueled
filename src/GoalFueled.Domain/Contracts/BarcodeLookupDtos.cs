using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// Result of scanning a product barcode. Either points at an ingredient already in the user's library
/// (a re-scan), carries prefill data pulled from Open Food Facts, or reports that nothing was found — in
/// which case the client offers the photograph-the-label fallback.
/// </summary>
public sealed record BarcodeLookupResultDto(
    /// <summary>The scanned barcode (digits only).</summary>
    string Barcode,
    /// <summary>True when either the library or OFF yielded usable product data.</summary>
    bool Found,
    /// <summary>
    /// Set when the user already has an ingredient with this barcode — the client should open it rather
    /// than create a duplicate. Null otherwise.
    /// </summary>
    string? ExistingIngredientId,
    /// <summary>Human-readable source of the data ("Open Food Facts" / "your library"); null when not found.</summary>
    string? Source,
    /// <summary>Product name, when known.</summary>
    string? ProductName,
    /// <summary>Brand(s), when known.</summary>
    string? Brands,
    /// <summary>URL of a product/label image to seed the ingredient's picture, when OFF has one.</summary>
    string? ImageUrl,
    /// <summary>The basis the nutrient values describe, in grams (always 100 when present).</summary>
    double BasisGrams,
    /// <summary>Catalog-keyed nutrient amounts per 100 g; empty when not found.</summary>
    IReadOnlyList<NutrientAmount> NutritionPer100g);

/// <summary>
/// Result of reading a photographed nutrition label with the vision model. Values are per the label's
/// stated serving, mapped onto this app's nutrient catalog, so the client can pre-fill the editor directly.
/// </summary>
public sealed record LabelOcrResultDto(
    /// <summary>Product name read from the packaging, when present.</summary>
    string? ProductName,
    /// <summary>The serving size the values describe.</summary>
    double ServingSize,
    /// <summary>The unit of the serving size.</summary>
    UnitOfMeasure Unit,
    /// <summary>Catalog-keyed nutrient amounts for one serving.</summary>
    IReadOnlyList<NutrientAmount> NutritionPerServing);
