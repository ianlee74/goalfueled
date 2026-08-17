using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Admin-only endpoints for user management: list all users, promote/demote the App Admin role, and
/// administer feature tags (subscription tier + individual feature grants/denials).
/// Every endpoint requires the caller to be acting as an admin (enforced in <see cref="UserAdminService"/>).
/// </summary>
public sealed class AdminUserFunctions
{
    private readonly UserAdminService _admin;
    private readonly IAuthenticator _auth;

    public AdminUserFunctions(UserAdminService admin, IAuthenticator auth)
    {
        _admin = admin;
        _auth = auth;
    }

    // NOTE: routes must NOT begin with "admin" — the Azure Functions host reserves that prefix
    // (a startsWith match, not an exact segment) for its own host endpoints, so even "admin-users"
    // fails to register and 404s. Use a prefix that doesn't start with "admin" (here "manage-users").
    [Function("ListUsers")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage-users")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _admin.ListAsync(user, req.HttpContext.RequestAborted)));

    [Function("SetUserAdminRole")]
    public Task<IActionResult> SetAdminRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage-users/{id}/admin-role")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<SetAdminRoleInput>(req, req.HttpContext.RequestAborted);
            var updated = await _admin.SetAdminRoleAsync(user, id, input.IsAdmin, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    /// <summary>Moves a user to a different subscription tier (changes their baseline feature tags).</summary>
    // Sub-routes of "manage-users/{id}" are safe from the route-shadowing landmine: that only bites when a
    // "{id}" route sits at the SAME depth as a literal sibling. There is no "GET manage-users/{id}", and
    // these are one segment deeper, so they resolve on their own — same shape as "admin-role" above.
    [Function("SetUserSubscriptionLevel")]
    public Task<IActionResult> SetSubscriptionLevel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage-users/{id}/subscription")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<SetSubscriptionLevelInput>(req, req.HttpContext.RequestAborted);
            var updated = await _admin.SetSubscriptionLevelAsync(user, id, input.Level, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    /// <summary>Grants, denies, or clears (inherit) one feature tag for one user.</summary>
    [Function("SetUserFeatureAccess")]
    public Task<IActionResult> SetFeatureAccess(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage-users/{id}/feature-access")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<SetFeatureAccessInput>(req, req.HttpContext.RequestAborted);
            var updated = await _admin.SetFeatureAccessAsync(user, id, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("DeleteUser")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage-users/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _admin.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
