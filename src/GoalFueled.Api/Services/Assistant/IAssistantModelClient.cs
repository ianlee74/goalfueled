using System.Text.Json;

namespace GoalFueled.Api.Services.Assistant;

/// <summary>
/// Abstraction over the external chat/tool-use model API, mirroring
/// <see cref="Nutrition.IFdcNutritionClient"/>: implemented by <see cref="AnthropicAssistantClient"/> in
/// production and faked in tests so the whole agentic loop, tool dispatch and confirmation flow can be
/// exercised with no network access and no API key.
/// </summary>
public interface IAssistantModelClient
{
    /// <summary>
    /// Whether an API key is configured. False means the feature degrades gracefully: the endpoint returns
    /// an honest "not configured" message instead of erroring, exactly as the USDA lookup does.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs one model turn over the supplied conversation and tool set. Implementations translate provider
    /// failures into <see cref="Common.ApiException"/>s rather than leaking transport exceptions.
    /// </summary>
    Task<AssistantModelReply> CompleteAsync(AssistantModelRequest request, CancellationToken ct = default);
}

/// <summary>One request to the model: the standing instructions, the tools it may call, and the transcript.</summary>
public sealed record AssistantModelRequest(
    string SystemPrompt,
    /// <summary>Tool definitions, already in the provider's schema shape (see <see cref="AssistantToolCatalog"/>).</summary>
    IReadOnlyList<object> Tools,
    /// <summary>The conversation so far. Each entry is a role plus that turn's raw content-block array.</summary>
    IReadOnlyList<(string Role, JsonElement Content)> Messages);

/// <summary>What the model returned for one turn.</summary>
public sealed record AssistantModelReply(
    /// <summary>Provider stop reason: <c>end_turn</c>, <c>tool_use</c>, <c>max_tokens</c>, <c>refusal</c>, …</summary>
    string StopReason,
    /// <summary>Concatenated text blocks, or null when the turn was tool calls only.</summary>
    string? Text,
    /// <summary>Tool calls the model made this turn, in the order they appeared.</summary>
    IReadOnlyList<AssistantToolCall> ToolCalls,
    /// <summary>
    /// The turn's raw content-block array. Echoed back verbatim on the next request so provider-owned
    /// blocks (thinking and its signature, in particular) survive the round-trip unmodified.
    /// </summary>
    JsonElement Content);

/// <summary>A single tool invocation requested by the model.</summary>
public sealed record AssistantToolCall(
    /// <summary>Provider-assigned id; the matching tool result must quote it back.</summary>
    string Id,
    string Name,
    /// <summary>The tool's arguments, as the model produced them. Always parsed, never string-matched.</summary>
    JsonElement Input);
