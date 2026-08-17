using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Domain.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoint for the barcode-scan fallback: read a photographed nutrition label into structured,
/// catalog-mapped nutrition the ingredient form can be pre-filled from.
/// </summary>
public sealed class NutritionLabelFunctions
{
    // Mirror the client-side accept list; the vision API supports these image types.
    private static readonly HashSet<string> AcceptedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif",
    };

    private const long MaxSizeBytes = 5 * 1024 * 1024;

    private readonly INutritionLabelReader _reader;
    private readonly VisionModelSettingsService _visionSettings;
    private readonly IAuthenticator _auth;

    public NutritionLabelFunctions(
        INutritionLabelReader reader, VisionModelSettingsService visionSettings, IAuthenticator auth)
    {
        _reader = reader;
        _visionSettings = visionSettings;
        _auth = auth;
    }

    // Distinct top-level route (NOT "ingredients/...") so it can't be shadowed by "ingredients/{id}".
    [Function("ReadNutritionLabel")]
    public Task<IActionResult> Read(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "nutrition-label-ocr")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            // Feature-tagged capability: label OCR must be enabled for this account.
            FeatureGuard.EnsureFeature(user, FeatureKeys.NutritionLabelOcr);

            if (!req.HasFormContentType)
                throw new ValidationException("Expected a multipart/form-data upload.");

            var file = req.Form.Files["file"];
            if (file is null)
                throw new ValidationException("No image was provided under the 'file' field.");
            if (!AcceptedTypes.Contains(file.ContentType))
                throw new ValidationException("Unsupported image type. Use JPEG, PNG, WebP, or GIF.");
            if (file.Length > MaxSizeBytes)
                throw new ValidationException("Image exceeds the 5 MB size limit.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, req.HttpContext.RequestAborted);

            // Honor an admin's runtime model override, if one is set.
            var modelOverride = await _visionSettings.GetOverrideAsync(req.HttpContext.RequestAborted);
            var result = await _reader.ReadAsync(
                ms.ToArray(), file.ContentType, modelOverride, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });
}
