using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Read-only endpoint for the feature-tag catalog (the admin "Feature tags" screen). The per-user side of
/// feature administration lives on the <c>manage-users/{id}/…</c> routes in
/// <see cref="AdminUserFunctions"/>; the signed-in user's own effective feature set rides along on
/// <c>GET /me</c> so the SPA never needs a second call to gate its UI.
/// </summary>
public sealed class FeatureFunctions
{
    private readonly UserAdminService _admin;
    private readonly IAuthenticator _auth;

    public FeatureFunctions(UserAdminService admin, IAuthenticator auth)
    {
        _admin = admin;
        _auth = auth;
    }

    // ROUTE-SHADOWING GUARD: this is deliberately a distinct TOP-LEVEL path ("feature-catalog") and NOT
    // "features/catalog". In the Functions isolated host a "GET features/{id}" route captures every
    // single-segment literal GET sibling under the same prefix (the literal is matched as {id} and answered
    // with that route's 404), which is why the repo already uses ingredient-nutrition-lookup,
    // recipe-review-queue, share-invitation-preview, etc. If a "GET features/{key}" endpoint is ever added,
    // this route must stay off that prefix.
    [Function("GetFeatureCatalog")]
    public Task<IActionResult> Catalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "feature-catalog")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, user =>
            Task.FromResult(HttpResults.Ok(_admin.ListFeatures(user))));
}
