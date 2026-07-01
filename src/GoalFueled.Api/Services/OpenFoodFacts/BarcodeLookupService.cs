using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Contracts;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Resolves a scanned barcode into either an existing library ingredient (a re-scan) or product data
/// pulled from Open Food Facts, mapped onto this app's nutrient catalog. The external call is behind
/// <see cref="IOffClient"/> so this orchestration is unit-testable without any network.
/// </summary>
public sealed class BarcodeLookupService
{
    /// <summary>The amount of food the returned values describe (OFF reports per 100 g).</summary>
    public const double BasisGrams = 100d;

    public const string OffSourceName = "Open Food Facts";
    public const string LibrarySourceName = "your library";

    private readonly IOffClient _client;
    private readonly IIngredientRepository _ingredients;

    public BarcodeLookupService(IOffClient client, IIngredientRepository ingredients)
    {
        _client = client;
        _ingredients = ingredients;
    }

    /// <summary>
    /// Looks up <paramref name="barcode"/>: first the user's own library (to avoid duplicates), then OFF.
    /// Throws <see cref="ValidationException"/> for a malformed barcode. A miss is not an error — it
    /// returns <see cref="BarcodeLookupResultDto.Found"/> = false so the client can offer label capture.
    /// </summary>
    public async Task<BarcodeLookupResultDto> LookupAsync(string userId, string? barcode, CancellationToken ct = default)
    {
        var normalized = Normalize(barcode);

        // 1) Already in the user's library? Point them at it rather than creating a duplicate.
        var existing = await _ingredients.GetByBarcodeVisibleAsync(userId, normalized, ct);
        if (existing is not null)
            return new BarcodeLookupResultDto(
                Barcode: normalized, Found: true, ExistingIngredientId: existing.Id,
                Source: LibrarySourceName, ProductName: existing.Name, Brands: null,
                ImageUrl: existing.ImageUrl, BasisGrams: BasisGrams,
                NutritionPer100g: Array.Empty<Domain.Nutrition.NutrientAmount>());

        // 2) Open Food Facts.
        var product = await _client.GetProductAsync(normalized, ct);
        if (product is not null)
        {
            var per100 = product.Nutriments is null
                ? Array.Empty<Domain.Nutrition.NutrientAmount>()
                : OffNutrimentMap.MapPer100g(product.Nutriments);

            return new BarcodeLookupResultDto(
                Barcode: normalized, Found: true, ExistingIngredientId: null,
                Source: OffSourceName,
                ProductName: string.IsNullOrWhiteSpace(product.ProductName) ? null : product.ProductName!.Trim(),
                Brands: string.IsNullOrWhiteSpace(product.Brands) ? null : product.Brands!.Trim(),
                ImageUrl: string.IsNullOrWhiteSpace(product.ImageNutritionUrl) ? product.ImageUrl : product.ImageNutritionUrl,
                BasisGrams: BasisGrams,
                NutritionPer100g: per100);
        }

        // 3) Not found anywhere — the client will offer the photograph-the-label path.
        return new BarcodeLookupResultDto(
            Barcode: normalized, Found: false, ExistingIngredientId: null,
            Source: null, ProductName: null, Brands: null, ImageUrl: null,
            BasisGrams: BasisGrams, NutritionPer100g: Array.Empty<Domain.Nutrition.NutrientAmount>());
    }

    /// <summary>
    /// Validates and normalizes a barcode to its digits. UPC/EAN codes are 8–14 digits; anything outside
    /// that is rejected so a stray scan doesn't hammer the OFF API with junk.
    /// </summary>
    private static string Normalize(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new ValidationException("Scan a barcode to look up.");
        var digits = new string(barcode.Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 14)
            throw new ValidationException("That doesn't look like a product barcode.");
        return digits;
    }
}
