using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GoalFueled.Api.Common;

/// <summary>Shared JSON options and helpers for producing consistent HTTP responses.</summary>
public static class HttpResults
{
    /// <summary>JSON options used across the API: camelCase, enums as strings, lenient casing.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes <paramref name="value"/> as a JSON 200 response.</summary>
    public static IActionResult Ok(object? value) => Json200(value, StatusCodes.Status200OK);

    /// <summary>Serializes <paramref name="value"/> as a JSON 201 response.</summary>
    public static IActionResult Created(object? value) => Json200(value, StatusCodes.Status201Created);

    public static IActionResult NoContent() => new StatusCodeResult(StatusCodes.Status204NoContent);

    /// <summary>Builds an RFC7807-style problem response for an <see cref="ApiException"/>.</summary>
    public static IActionResult Problem(ApiException ex) =>
        new ContentResult
        {
            StatusCode = ex.StatusCode,
            ContentType = "application/problem+json",
            Content = JsonSerializer.Serialize(new { status = ex.StatusCode, detail = ex.Message }, Json),
        };

    private static IActionResult Json200(object? value, int status) =>
        new ContentResult
        {
            StatusCode = status,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(value, Json),
        };

    /// <summary>Reads and deserializes a JSON request body, throwing <see cref="ValidationException"/> on bad input.</summary>
    public static async Task<T> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct = default)
    {
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, Json, ct);
            if (value is null) throw new ValidationException("Request body is required.");
            return value;
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"Invalid JSON: {ex.Message}");
        }
    }
}
