using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using GoalFueled.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace GoalFueled.Api.Services;

/// <summary>
/// Stores uploaded images in the public-read <c>images</c> blob container and returns their URL.
/// Locally this targets Azurite via <c>AzureWebJobsStorage = UseDevelopmentStorage=true</c>; in Azure
/// it uses the storage account provisioned by infra. Validation lives in <see cref="ImageUploadValidator"/>.
/// </summary>
public sealed class ImageStorageService
{
    private readonly BlobContainerClient _container;

    public ImageStorageService(IConfiguration config)
    {
        var connectionString = config["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("AzureWebJobsStorage connection string is not configured.");
        // Container name is configurable; defaults to the infra-provisioned "images" container.
        var containerName = config["GoalFueled:Storage:ImageContainer"] ?? "images";
        _container = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
    }

    /// <summary>
    /// Validates and uploads <paramref name="file"/>, returning the public blob URL.
    /// Ensures the container exists with public blob access (matching the bicep provisioning).
    /// </summary>
    public async Task<string> UploadAsync(IFormFile file, CancellationToken ct)
    {
        var extension = ImageUploadValidator.ValidateAndGetExtension(file.ContentType, file.Length);
        var blobName = ImageUploadValidator.NewBlobName(extension);

        await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        var blob = _container.GetBlobClient(blobName);
        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType } },
            ct);

        return blob.Uri.ToString();
    }
}
