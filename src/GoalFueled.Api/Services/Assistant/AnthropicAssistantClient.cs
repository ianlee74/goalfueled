using System.Net.Http.Json;
using System.Text.Json;
using GoalFueled.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services.Assistant;

/// <summary>
/// Talks to Claude's Messages API for the assistant's tool-use loop. The call is made server-side (never
/// from the browser) so the API key stays off the client, mirroring
/// <see cref="Nutrition.AnthropicVisionClient"/> and the USDA client before it.
/// </summary>
/// <remarks>
/// <para>Configuration:</para>
/// <list type="bullet">
/// <item><c>GoalFueled:Anthropic:ApiKey</c> — shared with label OCR. Absent ⇒ <see cref="IsConfigured"/> is
/// false and the assistant degrades to an honest "not configured" reply.</item>
/// <item><c>GoalFueled:Anthropic:AssistantModel</c> — defaults to <see cref="DefaultModel"/>.</item>
/// <item><c>GoalFueled:Anthropic:AssistantEffort</c> — <c>low</c>|<c>medium</c>|<c>high</c>|<c>xhigh</c>|
/// <c>max</c>, defaulting to <see cref="DefaultEffort"/>. This is the main cost/latency dial.</item>
/// <item><c>GoalFueled:Anthropic:BaseUrl</c> — shared with label OCR; defaults to the public API.</item>
/// </list>
/// <para><b>Request-shape notes</b> (this model family rejects several older parameters outright):
/// <c>temperature</c>/<c>top_p</c>/<c>top_k</c> and a fixed thinking <c>budget_tokens</c> are all removed —
/// sending any of them is a 400, so behaviour is steered by the system prompt and <c>effort</c> instead.
/// Thinking is ON by default and is deliberately left alone: explicitly disabling it on this model can make
/// the model write a tool call into its visible text (the call then silently never runs) and can leak
/// internal tags into the reply. <see cref="MaxTokens"/> is therefore sized to cover thinking AND the
/// response text, not just the text.</para>
/// </remarks>
public sealed class AnthropicAssistantClient : IAssistantModelClient
{
    /// <summary>Default assistant model. Strong at multi-step tool use, which is exactly this loop.</summary>
    public const string DefaultModel = "claude-opus-5";

    /// <summary>
    /// Default reasoning effort. Logging a meal is a short, well-scoped task with a small tool surface, so
    /// the mid setting buys the planning this needs without paying for depth it doesn't.
    /// </summary>
    public const string DefaultEffort = "medium";

    public const string DefaultBaseUrl = "https://api.anthropic.com";

    /// <summary>
    /// Per-call output ceiling. A hard cap the model is not aware of, covering thinking plus the reply, so
    /// one runaway turn can't run up an unbounded bill. Generous for a couple of tool calls and a sentence.
    /// </summary>
    public const int MaxTokens = 8_192;

    private static readonly HashSet<string> AllowedEfforts =
        new(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high", "xhigh", "max" };

    private readonly HttpClient _http;
    private readonly ILogger<AnthropicAssistantClient> _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _effort;
    private readonly Uri _baseUrl;

    public AnthropicAssistantClient(
        HttpClient http, IConfiguration config, ILogger<AnthropicAssistantClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GoalFueled:Anthropic:ApiKey"];

        var model = config["GoalFueled:Anthropic:AssistantModel"];
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        // An unrecognized effort would be a 400 on every call; fall back rather than break the feature.
        var effort = config["GoalFueled:Anthropic:AssistantEffort"]?.Trim();
        _effort = !string.IsNullOrWhiteSpace(effort) && AllowedEfforts.Contains(effort) ? effort : DefaultEffort;

        var baseUrl = config["GoalFueled:Anthropic:BaseUrl"];
        _baseUrl = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl, UriKind.Absolute);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<AssistantModelReply> CompleteAsync(
        AssistantModelRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ValidationException(AssistantService.UnavailableMessage);

        var body = new
        {
            model = _model,
            max_tokens = MaxTokens,
            // Effort is nested under output_config, not a top-level field.
            output_config = new { effort = _effort },
            system = request.SystemPrompt,
            tools = request.Tools,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
        };

        JsonElement root;
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/messages"))
            {
                Content = JsonContent.Create(body, options: HttpResults.Json),
            };
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
            httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var response = await _http.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Anthropic assistant call returned {Status}: {Detail}",
                    (int)response.StatusCode, detail);
                throw new NotFoundException(
                    "The assistant couldn't be reached just now. Please try again, or log it with the " +
                    "normal Fuel and Burn forms.");
            }

            root = await response.Content.ReadFromJsonAsync<JsonElement>(HttpResults.Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Anthropic assistant call failed.");
            throw new NotFoundException(
                "The assistant couldn't be reached just now. Please try again, or log it with the " +
                "normal Fuel and Burn forms.");
        }

        return Parse(root);
    }

    /// <summary>
    /// Projects the response onto <see cref="AssistantModelReply"/>: the stop reason, the concatenated text,
    /// the tool calls, and the content array kept verbatim for the next request's echo.
    /// </summary>
    private static AssistantModelReply Parse(JsonElement root)
    {
        var stopReason = root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String
            ? stop.GetString() ?? "end_turn"
            : "end_turn";

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            // A refusal can legitimately carry no content; anything else with no content is unusable.
            if (string.Equals(stopReason, "refusal", StringComparison.Ordinal))
                return new AssistantModelReply(stopReason, null, Array.Empty<AssistantToolCall>(), EmptyArray());

            throw new NotFoundException(
                "The assistant returned something unexpected. Please try again, or use the normal forms.");
        }

        var text = new List<string>();
        var toolCalls = new List<AssistantToolCall>();

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;

            switch (type.GetString())
            {
                case "text" when block.TryGetProperty("text", out var value) &&
                                 value.ValueKind == JsonValueKind.String:
                    var chunk = value.GetString();
                    if (!string.IsNullOrWhiteSpace(chunk)) text.Add(chunk.Trim());
                    break;

                case "tool_use":
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var name = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || !AssistantToolCatalog.IsKnown(name)) break;
                    var input = block.TryGetProperty("input", out var inputEl)
                        ? inputEl.Clone()
                        : EmptyObject();
                    toolCalls.Add(new AssistantToolCall(id!, name!, input));
                    break;
            }
        }

        return new AssistantModelReply(
            stopReason,
            text.Count == 0 ? null : string.Join("\n\n", text),
            toolCalls,
            // Clone so the element outlives the response document it was parsed from.
            content.Clone());
    }

    private static JsonElement EmptyArray() => JsonSerializer.SerializeToElement(Array.Empty<object>());

    private static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new { });
}
