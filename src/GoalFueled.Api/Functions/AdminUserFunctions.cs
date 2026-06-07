using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Admin-only endpoints for user management: list all users and promote/demote the App Admin role.
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

    [Function("ListUsers")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/users")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _admin.ListAsync(user, req.HttpContext.RequestAborted)));

    [Function("SetUserAdminRole")]
    public Task<IActionResult> SetAdminRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/users/{id}/admin-role")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<SetAdminRoleInput>(req, req.HttpContext.RequestAborted);
            var updated = await _admin.SetAdminRoleAsync(user, id, input.IsAdmin, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });
}
