namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Abstraction over the external USDA FoodData Central search API. Implemented by
/// <see cref="FdcNutritionClient"/> in production; faked in tests so the nutrient mapping logic
/// can be exercised against sample JSON without any network access.
/// </summary>
public interface IFdcNutritionClient
{
    /// <summary>
    /// Searches the external source for foods matching <paramref name="query"/> and returns the raw
    /// parsed response. Returns <c>null</c> when the source is unreachable, returns an error status
    /// (including rate-limiting), or yields no usable body — callers treat that as "no result".
    /// </summary>
    Task<FdcSearchResponse?> SearchAsync(string query, CancellationToken ct = default);
}
