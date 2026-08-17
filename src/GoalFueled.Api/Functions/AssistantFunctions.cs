using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Assistant;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoint for the AI assistant: one POST carries a sentence (typed, or spoken and transcribed in the
/// browser) plus the prior conversation, and comes back either with what was recorded or with a question to
/// confirm before anything is created.
/// </summary>
public sealed class AssistantFunctions
{
    private readonly AssistantService _assistant;
    private readonly IAuthenticator _auth;

    public AssistantFunctions(AssistantService assistant, IAuthenticator auth)
    {
        _assistant = assistant;
        _auth = auth;
    }

    // Distinct top-level route ("assistant-chat", NOT "assistant/chat"): a sibling `GET assistant/{id}` route
    // would capture single-segment literals at the same depth, which is how earlier features here got
    // shadowed. Same reason `ingredient-nutrition-lookup` and `recipe-review-queue` are spelled that way.
    // It also must not begin with "admin" — routes with that prefix fail to register at all.
    [Function("AssistantChat")]
    public Task<IActionResult> Chat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "assistant-chat")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var ct = req.HttpContext.RequestAborted;
            var input = await HttpResults.ReadJsonAsync<AssistantChatInput>(req, ct);
            // The feature gate (and the unconfigured-key degradation) live in the service, so they apply to
            // every caller of it rather than only to this endpoint.
            return HttpResults.Ok(await _assistant.ChatAsync(user, input, ct));
        });
}
