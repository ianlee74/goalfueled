using System.Net.Http.Json;
using GoalFueled.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Calls the public USDA FoodData Central <c>/foods/search</c> endpoint from the server (never the
/// browser) so the API key stays server-side and CORS is a non-issue.
/// </summary>
/// <remarks>
/// Configuration (all optional, with safe defaults):
/// <list type="bullet">
/// <item><c>GoalFueled:Nutrition:FdcApiKey</c> — defaults to <c>DEMO_KEY</c>, the public, rate-limited
/// key USDA provides for trial use. Set a personal key for higher limits.</item>
/// <item><c>GoalFueled:Nutrition:FdcBaseUrl</c> — defaults to <c>https://api.nal.usda.gov/fdc/v1/</c>.</item>
/// </list>
/// Every failure mode (no network, non-success status, rate-limit, malformed body) is swallowed and
/// surfaced as a <c>null</c> result so the lookup degrades gracefully and never blocks manual entry.
/// </remarks>
public sealed class FdcNutritionClient : IFdcNutritionClient
{
    public const string DefaultApiKey = "DEMO_KEY";
    public const string DefaultBaseUrl = "https://api.nal.usda.gov/fdc/v1/";

    private readonly HttpClient _http;
    private readonly ILogger<FdcNutritionClient> _logger;
    private readonly string _apiKey;
    private readonly Uri _baseUrl;

    public FdcNutritionClient(HttpClient http, IConfiguration config, ILogger<FdcNutritionClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = string.IsNullOrWhiteSpace(config["GoalFueled:Nutrition:FdcApiKey"])
            ? DefaultApiKey
            : config["GoalFueled:Nutrition:FdcApiKey"]!;
        var baseUrl = config["GoalFueled:Nutrition:FdcBaseUrl"];
        _baseUrl = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl, UriKind.Absolute);
    }

    public async Task<FdcSearchResponse?> SearchAsync(string query, CancellationToken ct = default)
    {
        // Cap the result set; "Foundation" + "SR Legacy" hold the cleanest per-100 g nutrient data.
        var requestUri = new Uri(
            _baseUrl,
            $"foods/search?api_key={Uri.EscapeDataString(_apiKey)}" +
            $"&query={Uri.EscapeDataString(query)}" +
            "&pageSize=5&dataType=Foundation,SR%20Legacy,Survey%20(FNDDS),Branded");

        try
        {
            using var response = await _http.GetAsync(requestUri, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "USDA FoodData Central lookup for '{Query}' returned {Status}.", query, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<FdcSearchResponse>(HttpResults.Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Network failure, timeout, or unparseable body: treat as "no result" so the UI can fall
            // back to manual entry with a friendly message rather than a 500.
            _logger.LogWarning(ex, "USDA FoodData Central lookup for '{Query}' failed.", query);
            return null;
        }
    }
}
