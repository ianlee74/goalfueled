using System.Text.Json.Serialization;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Minimal mirror of the USDA FoodData Central <c>/foods/search</c> response. Only the fields we
/// consume are modeled; the API returns much more. See https://fdc.nal.usda.gov/api-guide.html.
/// </summary>
public sealed class FdcSearchResponse
{
    [JsonPropertyName("foods")]
    public List<FdcFood> Foods { get; set; } = new();
}

/// <summary>A single matched food and its nutrient breakdown (per 100 g for "Foundation"/"SR" data).</summary>
public sealed class FdcFood
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    [JsonPropertyName("foodNutrients")]
    public List<FdcFoodNutrient> FoodNutrients { get; set; } = new();
}

/// <summary>
/// One nutrient measurement on a food. The search endpoint flattens the nutrient onto the item,
/// exposing the USDA nutrient number, name, unit and value directly.
/// </summary>
public sealed class FdcFoodNutrient
{
    /// <summary>USDA nutrient number as a string (e.g. "203" for Protein). Stable identifier we map on.</summary>
    [JsonPropertyName("nutrientNumber")]
    public string? NutrientNumber { get; set; }

    [JsonPropertyName("nutrientName")]
    public string? NutrientName { get; set; }

    /// <summary>Unit the value is expressed in, e.g. "G", "MG", "UG", "KCAL".</summary>
    [JsonPropertyName("unitName")]
    public string? UnitName { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}
