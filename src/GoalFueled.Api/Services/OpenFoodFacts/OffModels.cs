using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Minimal mirror of the Open Food Facts <c>/api/v2/product/{barcode}.json</c> response. Only the
/// fields we consume are modeled; the API returns far more. See https://openfoodfacts.github.io/.
/// </summary>
public sealed class OffProductResponse
{
    /// <summary>1 when the product exists, 0 when the barcode is unknown to OFF.</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("product")]
    public OffProduct? Product { get; set; }
}

/// <summary>A single product's descriptive fields and its nutriment map (values per 100 g / serving).</summary>
public sealed class OffProduct
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("brands")]
    public string? Brands { get; set; }

    /// <summary>Free-text serving size as printed on the label, e.g. "30 g" or "1 cup (240 ml)".</summary>
    [JsonPropertyName("serving_size")]
    public string? ServingSize { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("image_nutrition_url")]
    public string? ImageNutritionUrl { get; set; }

    /// <summary>
    /// The <c>nutriments</c> object. Keys are OFF nutriment ids with suffixes (e.g. "proteins_100g",
    /// "energy-kcal_100g", "salt_unit"). Values are a mix of numbers and strings, so they're kept as
    /// raw <see cref="JsonElement"/> and read numerically by <see cref="OffNutrimentMap"/>.
    /// </summary>
    [JsonPropertyName("nutriments")]
    public Dictionary<string, JsonElement>? Nutriments { get; set; }
}
