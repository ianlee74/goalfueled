using System.Net.Http.Json;
using GoalFueled.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Calls Open Food Facts from the server (never the browser) so credentials stay server-side and CORS is
/// a non-issue. Reads use the public v2 product endpoint; writes use the legacy <c>product_jqm2.pl</c>
/// form endpoint under a configured shared app account.
/// </summary>
/// <remarks>
/// Configuration (all optional, with safe defaults):
/// <list type="bullet">
/// <item><c>GoalFueled:OpenFoodFacts:ReadBaseUrl</c> — defaults to <c>https://world.openfoodfacts.org</c>.</item>
/// <item><c>GoalFueled:OpenFoodFacts:WriteBaseUrl</c> — defaults to the <b>staging</b> host
/// <c>https://world.openfoodfacts.net</c> so nothing hits production until a real host is set.</item>
/// <item><c>GoalFueled:OpenFoodFacts:UserAgent</c> — OFF requires a descriptive UA; defaults to
/// <c>GoalFueled/1.0 (ian@houseoflees.net)</c>.</item>
/// <item><c>GoalFueled:OpenFoodFacts:EnableContribution</c> — master switch for write-back, default
/// <b>false</b>. Contribution stays off until explicitly enabled, so data can be tested end-to-end
/// without ever pushing to OFF.</item>
/// <item><c>GoalFueled:OpenFoodFacts:UserId</c> / <c>:Password</c> — the shared write account. When either
/// is blank, <see cref="CanContribute"/> is false and writes are skipped.</item>
/// </list>
/// Every failure mode (no network, non-success status, malformed body) is swallowed and surfaced as a
/// <c>null</c>/<c>false</c> result so the feature degrades gracefully.
/// </remarks>
public sealed class OffClient : IOffClient
{
    public const string DefaultReadBaseUrl = "https://world.openfoodfacts.org";
    public const string DefaultWriteBaseUrl = "https://world.openfoodfacts.net";
    public const string DefaultUserAgent = "GoalFueled/1.0 (ian@houseoflees.net)";

    // A stable identifier for the app as an OFF contributor (helps OFF attribute app-sourced edits).
    private const string AppName = "GoalFueled";
    private const string AppVersion = "1.0";
    private const string AppUuid = "goalfueled-app";

    // OFF asks reads to request only the fields you need.
    private const string ReadFields =
        "product_name,brands,serving_size,serving_quantity,image_url,image_nutrition_url,nutriments";

    private readonly HttpClient _http;
    private readonly ILogger<OffClient> _logger;
    private readonly Uri _readBase;
    private readonly Uri _writeBase;
    private readonly bool _enableContribution;
    private readonly string? _userId;
    private readonly string? _password;

    public OffClient(HttpClient http, IConfiguration config, ILogger<OffClient> logger)
    {
        _http = http;
        _logger = logger;

        var ua = config["GoalFueled:OpenFoodFacts:UserAgent"];
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.Add(
            "User-Agent", string.IsNullOrWhiteSpace(ua) ? DefaultUserAgent : ua);

        _readBase = ResolveBase(config["GoalFueled:OpenFoodFacts:ReadBaseUrl"], DefaultReadBaseUrl);
        _writeBase = ResolveBase(config["GoalFueled:OpenFoodFacts:WriteBaseUrl"], DefaultWriteBaseUrl);
        _enableContribution = string.Equals(
            config["GoalFueled:OpenFoodFacts:EnableContribution"], "true", StringComparison.OrdinalIgnoreCase);
        _userId = config["GoalFueled:OpenFoodFacts:UserId"];
        _password = config["GoalFueled:OpenFoodFacts:Password"];
    }

    public bool CanContribute =>
        _enableContribution &&
        !string.IsNullOrWhiteSpace(_userId) && !string.IsNullOrWhiteSpace(_password);

    public async Task<OffProduct?> GetProductAsync(string barcode, CancellationToken ct = default)
    {
        var requestUri = new Uri(
            _readBase, $"/api/v2/product/{Uri.EscapeDataString(barcode)}.json?fields={ReadFields}");

        try
        {
            using var response = await _http.GetAsync(requestUri, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OFF product lookup for '{Barcode}' returned {Status}.", barcode, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<OffProductResponse>(HttpResults.Json, ct);
            // status == 1 and a product present means a real match; otherwise OFF didn't have it.
            return body is { Status: 1, Product: not null } ? body.Product : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "OFF product lookup for '{Barcode}' failed.", barcode);
            return null;
        }
    }

    public async Task<bool> ContributeAsync(
        string barcode, IReadOnlyList<KeyValuePair<string, string>> fields, CancellationToken ct = default)
    {
        if (!CanContribute) return false;

        var form = new List<KeyValuePair<string, string>>
        {
            new("code", barcode),
            new("user_id", _userId!),
            new("password", _password!),
            new("app_name", AppName),
            new("app_version", AppVersion),
            new("app_uuid", AppUuid),
        };
        form.AddRange(fields);

        var requestUri = new Uri(_writeBase, "/cgi/product_jqm2.pl");

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(requestUri, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OFF contribution for '{Barcode}' returned {Status}.", barcode, (int)response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "OFF contribution for '{Barcode}' failed.", barcode);
            return false;
        }
    }

    private static Uri ResolveBase(string? configured, string fallback) =>
        new(string.IsNullOrWhiteSpace(configured) ? fallback : configured, UriKind.Absolute);
}
