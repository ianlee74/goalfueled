using GoalFueled.Api.Common;
using GoalFueled.Api.Services;

namespace GoalFueled.Tests;

/// <summary>
/// Pure-logic tests for the image upload validator — no blob storage / Azurite required.
/// </summary>
public class ImageUploadValidatorTests
{
    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/gif", "gif")]
    [InlineData("IMAGE/JPEG", "jpg")]            // case-insensitive
    [InlineData("image/png; charset=binary", "png")] // tolerates parameters
    public void ValidateAndGetExtension_returns_expected_extension(string contentType, string expected)
    {
        Assert.Equal(expected, ImageUploadValidator.ValidateAndGetExtension(contentType, 1024));
    }

    [Theory]
    [InlineData("image/bmp")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateAndGetExtension_rejects_unsupported_types(string? contentType)
    {
        Assert.Throws<ValidationException>(() => ImageUploadValidator.ValidateAndGetExtension(contentType, 1024));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateAndGetExtension_rejects_empty(long length)
    {
        Assert.Throws<ValidationException>(() => ImageUploadValidator.ValidateAndGetExtension("image/png", length));
    }

    [Fact]
    public void ValidateAndGetExtension_rejects_oversized()
    {
        Assert.Throws<ValidationException>(() =>
            ImageUploadValidator.ValidateAndGetExtension("image/png", ImageUploadValidator.MaxSizeBytes + 1));
    }

    [Fact]
    public void ValidateAndGetExtension_accepts_exact_max_size()
    {
        Assert.Equal("png", ImageUploadValidator.ValidateAndGetExtension("image/png", ImageUploadValidator.MaxSizeBytes));
    }

    [Fact]
    public void NewBlobName_is_unique_and_uses_extension()
    {
        var a = ImageUploadValidator.NewBlobName("jpg");
        var b = ImageUploadValidator.NewBlobName("jpg");
        Assert.EndsWith(".jpg", a);
        Assert.NotEqual(a, b);
    }
}
