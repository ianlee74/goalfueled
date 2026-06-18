using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for account sharing: an owner creates/manages single-use invitation links; a recipient
/// previews and accepts/rejects one; and any signed-in user lists the accounts they can switch into.
///
/// <para>Routing note: the token-based recipient endpoints use DISTINCT top-level paths
/// (<c>share-invitation-preview/{token}</c>, <c>share-invitation-respond/{token}</c>) rather than living
/// under <c>share-invitations/{token}</c>. A <c>GET share-invitations/{token}</c> route would shadow the
/// literal <c>GET share-invitations</c> sibling (the Functions host treats a single-segment <c>{token}</c>
/// route as capturing the bare collection path), so the segment paths are kept separate.</para>
/// </summary>
public sealed class ShareFunctions
{
    private readonly SharingService _sharing;
    private readonly IAuthenticator _auth;

    public ShareFunctions(SharingService sharing, IAuthenticator auth)
    {
        _sharing = sharing;
        _auth = auth;
    }

    [Function("CreateShareInvitation")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "share-invitations")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<CreateShareInvitationInput>(req, req.HttpContext.RequestAborted);
            var created = await _sharing.CreateAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(created);
        });

    [Function("ListShareInvitations")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "share-invitations")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _sharing.ListMineAsync(user, req.HttpContext.RequestAborted)));

    [Function("DeleteShareInvitation")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "share-invitations/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _sharing.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });

    [Function("PreviewShareInvitation")]
    public Task<IActionResult> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "share-invitation-preview/{token}")] HttpRequest req,
        string token) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _sharing.PreviewAsync(user, token, req.HttpContext.RequestAborted)));

    [Function("RespondShareInvitation")]
    public Task<IActionResult> Respond(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "share-invitation-respond/{token}")] HttpRequest req,
        string token) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<RespondInput>(req, req.HttpContext.RequestAborted);
            var result = await _sharing.RespondAsync(user, token, input.Accept, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    /// <summary>The accounts the signed-in user can switch into (their own + accounts shared with them).</summary>
    [Function("ListSharedAccounts")]
    public Task<IActionResult> SharedAccounts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shared-accounts")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _sharing.ListSwitchableAccountsAsync(user, req.HttpContext.RequestAborted)));

    /// <summary>Payload for accepting (true) or rejecting (false) an invitation.</summary>
    private sealed record RespondInput(bool Accept);
}
