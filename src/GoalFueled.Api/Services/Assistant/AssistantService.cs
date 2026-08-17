using System.Text;
using System.Text.Json;
using GoalFueled.Api.Common;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;

namespace GoalFueled.Api.Services.Assistant;

/// <summary>
/// The AI assistant: turns a sentence ("I ate a quarter pounder with fries from McDonalds") into real,
/// persisted GoalFueled data by running a capped tool-use loop against the model and executing each
/// requested tool through <see cref="AssistantToolDispatcher"/>.
/// </summary>
/// <remarks>
/// <para><b>Where conversation state lives.</b> Nowhere on the server. Each response returns the full
/// transcript and the client posts it back on the next turn — the same statelessness the Messages API
/// itself has. That avoids a new persisted entity (and the Cosmos rules that come with one), avoids
/// session expiry/cleanup, and keeps the API horizontally scalable. The trade-off is that the transcript is
/// untrusted input: it is size-capped and shape-validated here, and it is never used for authorization —
/// every tool re-resolves and re-authorizes through the service layer, so a forged transcript can only
/// mislead the model, never widen what the caller may do.</para>
///
/// <para><b>How "prompt if unsure" works.</b> Confirmation is a tool, not a convention. When the model calls
/// <see cref="AssistantToolCatalog.AskUserToConfirm"/> the loop stops, returns
/// <see cref="AssistantChatStatus.NeedsConfirmation"/> with that call's id, and the user's reply comes back
/// as that call's tool result — so the pause rides the normal tool-use protocol and the model resumes
/// exactly where it paused, with full context.</para>
///
/// <para><b>Cost and safety caps.</b> A turn is bounded by <see cref="MaxIterations"/> model calls; each
/// call is bounded by <c>max_tokens</c> and an effort setting in <see cref="AnthropicAssistantClient"/>;
/// the request message and the echoed conversation are bounded by <see cref="InputLimits"/>. With no API key
/// configured the feature degrades to an honest "not configured" reply rather than an error.</para>
/// </remarks>
public sealed class AssistantService
{
    /// <summary>
    /// Max model calls in one user turn. Each iteration is one billed request, so this is the primary cost
    /// ceiling: a pathological prompt costs at most this many calls, then the user gets a plain answer.
    /// Enough for the worked example (search → look up nutrition → confirm → create ×3 → log → summarize).
    /// </summary>
    public const int MaxIterations = 10;

    private readonly IAssistantModelClient _client;
    private readonly AssistantToolDispatcher _dispatcher;

    public AssistantService(IAssistantModelClient client, AssistantToolDispatcher dispatcher)
    {
        _client = client;
        _dispatcher = dispatcher;
    }

    /// <summary>The message shown when no API key is configured. Honest, and never a crash or silent no-op.</summary>
    public const string UnavailableMessage =
        "The AI assistant isn't configured on this server yet, so I can't take that down for you. " +
        "You can still log it with the normal Fuel and Burn forms.";

    /// <summary>Runs one user turn of the assistant conversation.</summary>
    public async Task<AssistantChatResultDto> ChatAsync(
        AppUser user, AssistantChatInput input, CancellationToken ct = default)
    {
        // Feature-tagged capability, enforced before anything else (and before any billable call).
        FeatureGuard.EnsureFeature(user, FeatureKeys.AiVoiceLogging);

        var message = input.Message?.Trim();
        InputLimits.RequireText(message, InputLimits.AssistantMessageMaxLength, "Message");

        // Graceful degradation, matching the USDA lookup: no key configured is a normal answer, not a 500.
        if (!_client.IsConfigured)
            return new AssistantChatResultDto(
                AssistantChatStatus.Unavailable, UnavailableMessage,
                Array.Empty<AssistantActionDto>(), Array.Empty<AssistantTurnDto>());

        var turns = Normalize(input.Conversation);

        // Either resume a paused confirmation (the answer becomes that tool call's result) or open a new turn.
        if (!string.IsNullOrWhiteSpace(input.PendingToolUseId))
            AppendConfirmationAnswer(turns, input.PendingToolUseId!.Trim(), message!);
        else
            turns.Add(new AssistantTurnDto("user", TextContent(message!)));

        var actions = new List<AssistantActionDto>();

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var reply = await _client.CompleteAsync(
                new AssistantModelRequest(
                    SystemPrompt(),
                    AssistantToolCatalog.Definitions,
                    turns.Select(t => (t.Role, t.Content)).ToList()),
                ct);

            // A safety decline arrives as a normal successful response with no usable content — check the
            // stop reason BEFORE reading the reply, and answer honestly instead of echoing an empty string.
            if (string.Equals(reply.StopReason, "refusal", StringComparison.Ordinal))
                return Completed(
                    "I can't help with that one. Try describing what you ate or did in plain terms, " +
                    "or use the normal Fuel and Burn forms.",
                    actions, turns);

            turns.Add(new AssistantTurnDto("assistant", reply.Content));

            if (reply.ToolCalls.Count == 0)
                return Completed(Fallback(reply.Text, "Done."), actions, turns);

            // Execute the turn's tool calls in order, stopping at a confirmation request.
            var results = new List<object>();
            string? pendingId = null;
            string? question = null;

            foreach (var call in reply.ToolCalls)
            {
                if (pendingId is not null)
                {
                    // Everything after a confirmation request is deliberately NOT run: the user's answer may
                    // change what should happen. The model is told so it can re-issue those calls after.
                    results.Add(ToolResult(call.Id,
                        "{\"error\":\"Not executed — you asked the user a question first. Re-issue this call " +
                        "after their answer if it still applies.\"}", isError: true));
                    continue;
                }

                if (call.Name == AssistantToolCatalog.AskUserToConfirm)
                {
                    pendingId = call.Id;
                    question = BuildQuestion(call.Input, reply.Text);
                    continue; // no result yet — the user supplies it on the next request.
                }

                var outcome = await _dispatcher.ExecuteAsync(user, call, ct);
                if (outcome.Action is not null) actions.Add(outcome.Action);
                results.Add(ToolResult(call.Id, outcome.ResultJson, outcome.IsError));
            }

            // The tool results for one assistant turn must arrive as a SINGLE user message; when a
            // confirmation is pending that message is deliberately incomplete and is finished on resume.
            if (results.Count > 0)
                turns.Add(new AssistantTurnDto("user", ToJson(results)));

            if (pendingId is not null)
                return new AssistantChatResultDto(
                    AssistantChatStatus.NeedsConfirmation, question!, actions, turns, pendingId);
        }

        // Loop cap reached: stop spending, and say plainly what did happen rather than pretending to finish.
        return Completed(
            actions.Count > 0
                ? "I stopped there to avoid going in circles. Here's what I did get done — please check it."
                : "I couldn't work that out in a reasonable number of steps. Try describing it more simply, " +
                  "or log it with the normal forms.",
            actions, turns);
    }

    private static AssistantChatResultDto Completed(
        string reply, IReadOnlyList<AssistantActionDto> actions, IReadOnlyList<AssistantTurnDto> turns) =>
        new(AssistantChatStatus.Completed, reply, actions, turns);

    private static string Fallback(string? text, string ifEmpty) =>
        string.IsNullOrWhiteSpace(text) ? ifEmpty : text.Trim();

    /// <summary>Builds the question shown to the user, preferring the tool's own wording.</summary>
    private static string BuildQuestion(JsonElement input, string? assistantText)
    {
        string? Read(string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

        var question = Read("question") ?? Fallback(assistantText, "Shall I go ahead?");
        var details = Read("details");
        return details is null ? question : $"{question}\n\n{details}";
    }

    // ---- transcript handling ----------------------------------------------------------------------

    /// <summary>
    /// Validates and size-caps the client-echoed conversation. Shape only — nothing here is trusted for
    /// authorization; see the class remarks.
    /// </summary>
    private static List<AssistantTurnDto> Normalize(IReadOnlyList<AssistantTurnDto>? conversation)
    {
        if (conversation is null || conversation.Count == 0) return new List<AssistantTurnDto>();

        InputLimits.LimitCount(conversation.Count, InputLimits.AssistantConversationTurnsMax, "conversation turns");

        var turns = new List<AssistantTurnDto>(conversation.Count);
        var totalChars = 0;
        foreach (var turn in conversation)
        {
            if (turn.Role is not ("user" or "assistant"))
                throw new ValidationException("Conversation turns must have a role of 'user' or 'assistant'.");
            if (turn.Content.ValueKind != JsonValueKind.Array)
                throw new ValidationException("Each conversation turn must carry an array of content blocks.");

            var raw = turn.Content.GetRawText();
            totalChars += raw.Length;
            if (totalChars > InputLimits.AssistantConversationCharsMax)
                throw new ValidationException(
                    "This conversation has grown too long. Start a new one and describe it in one go.");

            turns.Add(turn);
        }
        return turns;
    }

    /// <summary>
    /// Completes a paused confirmation by supplying the user's answer as the pending tool call's result.
    /// The pending call is the last block of the last assistant turn, so the result either extends the
    /// partially-built user turn that follows it or starts a new one.
    /// </summary>
    private static void AppendConfirmationAnswer(List<AssistantTurnDto> turns, string pendingToolUseId, string answer)
    {
        if (turns.Count == 0)
            throw new ValidationException("There is no question to answer. Start a new conversation.");

        var result = ToolResult(pendingToolUseId, JsonSerializer.Serialize(answer, HttpResults.Json), isError: false);
        var last = turns[^1];

        if (last.Role == "user")
        {
            // Tool results already computed this turn — append to that same user message.
            var blocks = last.Content.EnumerateArray().Select(b => (object)b).ToList();
            blocks.Add(result);
            turns[^1] = new AssistantTurnDto("user", ToJson(blocks));
            return;
        }

        turns.Add(new AssistantTurnDto("user", ToJson(new List<object> { result })));
    }

    private static object ToolResult(string toolUseId, string contentJson, bool isError) => new
    {
        type = "tool_result",
        tool_use_id = toolUseId,
        // The provider takes tool_result content as text; our payloads are JSON documents rendered as text.
        content = contentJson,
        is_error = isError,
    };

    private static JsonElement TextContent(string text) =>
        ToJson(new List<object> { new { type = "text", text } });

    /// <summary>Serializes content blocks into the detached <see cref="JsonElement"/> a turn carries.</summary>
    private static JsonElement ToJson(IReadOnlyList<object> blocks) =>
        JsonSerializer.SerializeToElement(blocks, HttpResults.Json);

    // ---- prompt -----------------------------------------------------------------------------------

    /// <summary>
    /// The standing instructions. Deliberately states the domain model and the workflow, not a step-by-step
    /// script — and asks for brevity and tight scope, which is where this model needs steering.
    /// </summary>
    private static string SystemPrompt()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var prompt = new StringBuilder();
        prompt.Append(
            "You are the GoalFueled logging assistant. The user tells you, in one sentence, what they ate or " +
            "what exercise they did, and you record it accurately using the tools.\n\n");

        prompt.Append("Today's date is ").Append(today).Append(" (UTC). Dates are never in the future.\n\n");

        prompt.Append(
            "The data model, innermost first:\n" +
            "- An INGREDIENT is one food with nutrition for one serving of a stated size.\n" +
            "- A RECIPE composes ingredient quantities into a dish and yields a number of servings.\n" +
            "- A FUEL SOURCE is what the user actually logs as fuel; it is made of portions of recipes.\n" +
            "- Logging FUEL records eating a fuel source (or a free-form item). Logging BURN records exercise.\n\n");

        prompt.Append(
            "How to work:\n" +
            "- Search before you create. If an existing fuel source clearly matches what they described, log it.\n" +
            "- If nothing matches, build what is missing bottom-up: ingredients, then a recipe, then a fuel " +
            "source, then log it. Ground nutrition with lookup_ingredient_nutrition rather than inventing it; " +
            "if the lookup finds nothing, estimate and say in your final message that you estimated.\n" +
            "- Call ask_user_to_confirm, on its own, before creating anything, and whenever you are not " +
            "confident which existing item they meant. Say plainly what you propose to create or log. Do not " +
            "ask about things you can reasonably decide yourself, such as a sensible serving size.\n" +
            "- Everything you create belongs to this user and is private to them.\n\n");

        prompt.Append(
            "Your final message is what the user reads. Keep it to a sentence or two: what you recorded and " +
            "anything they should check. Do not list every tool call, do not restate the nutrition table, and " +
            "do not offer follow-up work they did not ask for. Deliver what was asked at the scope asked — if " +
            "something can't be done, do the rest and say plainly what is missing.");

        return prompt.ToString();
    }
}
