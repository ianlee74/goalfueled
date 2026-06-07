using GoalFueled.Api.Common;

namespace GoalFueled.Api.Services;

/// <summary>
/// Pure validation/mapping for image uploads — no blob I/O so it is unit-testable without an emulator.
/// Allows the web-friendly image formats and caps the size to keep the public container tidy.
/// </summary>
public static class ImageUploadValidator
{
    /// <summary>Maximum accepted upload size in bytes (5 MB).</summary>
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    // Content-type → file extension for the formats we accept.
    private static readonly IReadOnlyDictionary<string, string> ExtensionByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/png"] = "png",
            ["image/webp"] = "webp",
            ["image/gif"] = "gif",
        };

    /// <summary>
    /// Validates the upload and returns the canonical file extension (no dot) for the content type.
    /// Throws <see cref="ValidationException"/> when the type is unsupported or the size is out of range.
    /// </summary>
    public static string ValidateAndGetExtension(string? contentType, long length)
    {
        if (length <= 0)
            throw new ValidationException("The uploaded file is empty.");
        if (length > MaxSizeBytes)
            throw new ValidationException($"Image exceeds the {MaxSizeBytes / (1024 * 1024)} MB size limit.");

        // Strip any "; charset=" / parameters the browser may append.
        var type = (contentType ?? string.Empty).Split(';')[0].Trim();
        if (!ExtensionByContentType.TryGetValue(type, out var extension))
            throw new ValidationException("Unsupported image type. Use JPEG, PNG, WebP, or GIF.");

        return extension;
    }

    /// <summary>Builds a unique, collision-free blob name like <c>{guid}.{ext}</c>.</summary>
    public static string NewBlobName(string extension) => $"{Guid.NewGuid():N}.{extension}";
}
