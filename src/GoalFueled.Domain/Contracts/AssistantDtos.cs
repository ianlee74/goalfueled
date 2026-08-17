using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// One message in an assistant conversation, carried verbatim between the client and the model API.
/// </summary>
/// <remarks>
/// <para><b>Why the content is raw JSON.</b> A model turn can contain block types this app does not model
/// (thinking blocks and their signatures, for example) which must be echoed back <em>unchanged</em> on the
/// next turn or the provider rejects the request. Storing the provider's content array as an opaque
/// <see cref="JsonElement"/> makes that round-trip byte-exact and keeps this app out of the business of
/// re-serializing block shapes it doesn't own.</para>
/// <para><b>Why it is safe to hand to the browser.</b> The conversation is client-held state (see
/// <c>AssistantService</c>) — there is no server-side session store. A caller could therefore forge a
/// transcript, but that buys them nothing: every tool the assistant can invoke runs through the normal
/// service layer under the caller's own identity, so a fabricated transcript can only mislead the model,
/// never bypass ownership, visibility, validation, or limits. Nothing here is ever treated as
/// authorization.</para>
/// </remarks>
public sealed record AssistantTurnDto(
    /// <summary>Either <c>user</c> or <c>assistant</c>; any other value is rejected.</summary>
    string Role,
    /// <summary>The provider's content-block array for this turn, passed through untouched.</summary>
    JsonElement Content);

/// <summary>How a single assistant exchange ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssistantChatStatus
{
    /// <summary>The assistant finished its turn; <see cref="AssistantChatResultDto.Reply"/> is its answer.</summary>
    Completed = 0,

    /// <summary>
    /// The assistant needs the user to confirm something before it acts (an uncertain match, or anything it
    /// wants to create). The client shows <see cref="AssistantChatResultDto.Reply"/> as the question and
    /// posts the user's answer back with the returned conversation and
    /// <see cref="AssistantChatResultDto.PendingToolUseId"/>.
    /// </summary>
    NeedsConfirmation = 1,

    /// <summary>
    /// The assistant is not available (no API key configured, or the provider could not be reached). The
    /// reply explains it honestly; the caller should fall back to the normal forms.
    /// </summary>
    Unavailable = 2
}

/// <summary>
/// One thing the assistant actually did during a turn, so the UI can show real, verifiable outcomes rather
/// than trusting the model's prose. Only successful, data-changing tool calls produce an action.
/// </summary>
public sealed record AssistantActionDto(
    /// <summary>Stable machine key: <c>ingredient</c>, <c>recipe</c>, <c>fuel-source</c>, <c>fuel</c>, <c>burn</c>.</summary>
    string Kind,
    /// <summary>Short human-readable sentence, e.g. "Logged 1 serving of Quarter Pounder with fries".</summary>
    string Summary,
    /// <summary>Id of the created/affected record, when there is one.</summary>
    string? Id = null);

/// <summary>
/// Payload for one turn of the assistant conversation. The first turn sends just
/// <see cref="Message"/>; every later turn echoes back the <see cref="Conversation"/> the previous
/// response returned (plus <see cref="PendingToolUseId"/> when answering a confirmation question).
/// </summary>
public sealed record AssistantChatInput(
    /// <summary>What the user typed or said. Required, and length-capped.</summary>
    string? Message,
    /// <summary>Prior turns, exactly as returned by the previous response. Omit to start fresh.</summary>
    IReadOnlyList<AssistantTurnDto>? Conversation = null,
    /// <summary>
    /// Set only when answering a confirmation: the id from the previous response's
    /// <see cref="AssistantChatResultDto.PendingToolUseId"/>. <see cref="Message"/> then carries the answer.
    /// </summary>
    string? PendingToolUseId = null);

/// <summary>The result of one assistant turn.</summary>
public sealed record AssistantChatResultDto(
    AssistantChatStatus Status,
    /// <summary>The assistant's message — its answer, or its confirmation question.</summary>
    string Reply,
    /// <summary>Everything the assistant actually created or logged this turn (possibly empty).</summary>
    IReadOnlyList<AssistantActionDto> Actions,
    /// <summary>
    /// The full conversation so far. The client stores this and sends it back on the next turn; the server
    /// keeps no session state of its own.
    /// </summary>
    IReadOnlyList<AssistantTurnDto> Conversation,
    /// <summary>
    /// When <see cref="Status"/> is <see cref="AssistantChatStatus.NeedsConfirmation"/>, the id of the
    /// question awaiting an answer. Null otherwise.
    /// </summary>
    string? PendingToolUseId = null);
