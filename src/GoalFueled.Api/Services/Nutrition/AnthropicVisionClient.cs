using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GoalFueled.Api.Common;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Nutrition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Reads a nutrition label photo with Claude's vision model. The image is sent server-side (never from
/// the browser) so the API key stays off the client, and a strict tool-use schema forces the model to
/// return structured, catalog-keyed nutrition instead of prose we'd have to parse.
/// </summary>
/// <remarks>
/// Configuration:
/// <list type="bullet">
/// <item><c>GoalFueled:Anthropic:ApiKey</c> — required to enable label reading. When absent, the endpoint
/// tells the user to enter values manually.</item>
/// <item><c>GoalFueled:Anthropic:VisionModel</c> — defaults to <c>claude-opus-4-8</c>. Set a cheaper model
/// (e.g. <c>claude-sonnet-5</c>, <c>claude-haiku-4-5</c>) to trade some accuracy for cost.</item>
/// <item><c>GoalFueled:Anthropic:BaseUrl</c> — defaults to <c>https://api.anthropic.com</c>.</item>
/// </list>
/// </remarks>
public sealed class AnthropicVisionClient : INutritionLabelReader
{
    public const string DefaultModel = "claude-opus-4-8";
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string ToolName = "record_nutrition_label";

    private readonly HttpClient _http;
    private readonly ILogger<AnthropicVisionClient> _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly Uri _baseUrl;

    public AnthropicVisionClient(HttpClient http, IConfiguration config, ILogger<AnthropicVisionClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GoalFueled:Anthropic:ApiKey"];
        var model = config["GoalFueled:Anthropic:VisionModel"];
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        var baseUrl = config["GoalFueled:Anthropic:BaseUrl"];
        _baseUrl = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl, UriKind.Absolute);
    }

    public async Task<LabelOcrResultDto> ReadAsync(
        byte[] imageBytes, string mediaType, string? modelOverride = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ValidationException(
                "Reading nutrition labels isn't available right now. Please enter the values manually.");

        // An admin may override the model at runtime (for A/B testing) before a choice is baked into config.
        var model = string.IsNullOrWhiteSpace(modelOverride) ? _model : modelOverride!.Trim();
        var requestBody = BuildRequest(imageBytes, mediaType, model);

        JsonElement root;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/messages"))
            {
                Content = JsonContent.Create(requestBody, options: HttpResults.Json),
            };
            request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Anthropic vision call returned {Status}: {Detail}",
                    (int)response.StatusCode, detail);
                throw new NotFoundException(
                    "Couldn't read the nutrition label. Try a clearer photo, or enter the values manually.");
            }

            root = (await response.Content.ReadFromJsonAsync<JsonElement>(HttpResults.Json, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Anthropic vision call failed.");
            throw new NotFoundException(
                "Couldn't reach the label reader. Please try again, or enter the values manually.");
        }

        return Parse(root);
    }

    /// <summary>
    /// Builds the Messages API request: the image plus a prompt, with a strict tool the model must call,
    /// whose schema constrains the serving unit and nutrient ids to this app's catalog.
    /// </summary>
    private object BuildRequest(byte[] imageBytes, string mediaType, string model) => new
    {
        model,
        max_tokens = 1024,
        tool_choice = new { type = "tool", name = ToolName },
        tools = new object[]
        {
            new
            {
                name = ToolName,
                description = "Record the Nutrition Facts read from a product label, for a single serving.",
                strict = true,
                input_schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "servingSize", "unit", "nutrients" },
                    properties = new
                    {
                        productName = new { type = "string", description = "The product name, if printed." },
                        servingSize = new { type = "number", description = "The serving size amount (e.g. 30)." },
                        unit = new
                        {
                            type = "string",
                            description = "The unit of the serving size.",
                            @enum = Enum.GetNames<UnitOfMeasure>(),
                        },
                        nutrients = new
                        {
                            type = "array",
                            description = "One entry per nutrient printed on the label, for one serving.",
                            items = new
                            {
                                type = "object",
                                additionalProperties = false,
                                required = new[] { "nutrientId", "value" },
                                properties = new
                                {
                                    nutrientId = new
                                    {
                                        type = "string",
                                        @enum = NutrientCatalog.All.Select(n => n.Id).ToArray(),
                                    },
                                    value = new { type = "number", description = "Amount per serving, in the nutrient's usual unit." },
                                },
                            },
                        },
                    },
                },
            },
        },
        messages = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = mediaType,
                            data = Convert.ToBase64String(imageBytes),
                        },
                    },
                    new
                    {
                        type = "text",
                        text =
                            "Read the Nutrition Facts panel in this photo and record the values for ONE serving " +
                            "using the tool. Use the serving size and unit printed on the label. Report each " +
                            "nutrient in its usual label unit (Calories in kcal; fats, carbs, protein, fiber, " +
                            "sugars in grams; sodium, cholesterol, calcium, etc. in milligrams; vitamin A/D in " +
                            "micrograms). Only include nutrients actually printed on the label.",
                    },
                },
            },
        },
    };

    /// <summary>Extracts the tool call from the response and maps it onto a validated OCR result.</summary>
    private LabelOcrResultDto Parse(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw NotReadable();

        JsonElement? input = null;
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "tool_use" &&
                block.TryGetProperty("name", out var n) && n.GetString() == ToolName &&
                block.TryGetProperty("input", out var i))
            {
                input = i;
                break;
            }
        }

        if (input is not { } data)
            throw NotReadable();

        var productName = data.TryGetProperty("productName", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString()?.Trim()
            : null;

        var servingSize = data.TryGetProperty("servingSize", out var ss) && ss.TryGetDouble(out var s) && s > 0
            ? s
            : 1d;

        var unit = UnitOfMeasure.Gram;
        if (data.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String &&
            Enum.TryParse<UnitOfMeasure>(u.GetString(), out var parsedUnit))
            unit = parsedUnit;

        var amounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (data.TryGetProperty("nutrients", out var nutrients) && nutrients.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in nutrients.EnumerateArray())
            {
                if (!entry.TryGetProperty("nutrientId", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    continue;
                var id = idEl.GetString();
                if (id is null || !NutrientCatalog.IsKnown(id) || amounts.ContainsKey(id)) continue;
                if (!entry.TryGetProperty("value", out var valEl) || !valEl.TryGetDouble(out var val) || val < 0)
                    continue;
                amounts[id] = val;
            }
        }

        if (amounts.Count == 0)
            throw NotReadable();

        // Preserve catalog order for stable, label-like output.
        var ordered = NutrientCatalog.All
            .Where(def => amounts.ContainsKey(def.Id))
            .Select(def => new NutrientAmount(def.Id, amounts[def.Id]))
            .ToList();

        return new LabelOcrResultDto(
            ProductName: string.IsNullOrWhiteSpace(productName) ? null : productName,
            ServingSize: servingSize, Unit: unit, NutritionPerServing: ordered);
    }

    private static NotFoundException NotReadable() => new(
        "Couldn't read any nutrition values from that photo. Try a clearer, straight-on shot of the " +
        "Nutrition Facts panel, or enter the values manually.");
}
