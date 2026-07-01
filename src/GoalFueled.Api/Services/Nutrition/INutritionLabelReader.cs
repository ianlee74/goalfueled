using GoalFueled.Domain.Contracts;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Reads a photographed nutrition label into structured, catalog-mapped nutrition. Implemented by
/// <see cref="AnthropicVisionClient"/> in production; the abstraction keeps the OCR endpoint and its
/// callers decoupled from the vision provider and lets the mapping be faked in tests.
/// </summary>
public interface INutritionLabelReader
{
    /// <summary>
    /// Extracts per-serving nutrition from a label image. Throws an <see cref="Common.ApiException"/>
    /// (with a user-facing message) when reading isn't configured or the label couldn't be read, so the
    /// client can fall back to manual entry.
    /// </summary>
    /// <param name="modelOverride">
    /// When set, the model to use for this call (an admin runtime override); otherwise the configured model.
    /// </param>
    Task<LabelOcrResultDto> ReadAsync(
        byte[] imageBytes, string mediaType, string? modelOverride = null, CancellationToken ct = default);
}
