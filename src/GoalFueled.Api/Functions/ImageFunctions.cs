using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>HTTP endpoint for uploading ingredient/recipe images into blob storage.</summary>
public sealed class ImageFunctions
{
    private readonly ImageStorageService _images;
    private readonly IAuthenticator _auth;

    public ImageFunctions(ImageStorageService images, IAuthenticator auth)
    {
        _images = images;
        _auth = auth;
    }

    [Function("UploadImage")]
    public Task<IActionResult> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "images")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async _ =>
        {
            // ASP.NET Core integration gives us a real HttpRequest; the file arrives as multipart/form-data.
            if (!req.HasFormContentType)
                throw new ValidationException("Expected a multipart/form-data upload.");

            var file = req.Form.Files["file"];
            if (file is null)
                throw new ValidationException("No file was provided under the 'file' field.");

            var url = await _images.UploadAsync(file, req.HttpContext.RequestAborted);
            return HttpResults.Ok(new { url });
        });
}
