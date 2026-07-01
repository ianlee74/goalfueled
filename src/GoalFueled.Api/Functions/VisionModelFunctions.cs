using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Admin-only endpoints for choosing the nutrition-label OCR vision model at runtime. Lets an admin try
/// different models before committing a choice to configuration. Routes are a distinct top-level path
/// (and don't start with the reserved "admin" prefix).
/// </summary>
public sealed class VisionModelFunctions
{
    private readonly VisionModelSettingsService _settings;
    private readonly IAuthenticator _auth;

    public VisionModelFunctions(VisionModelSettingsService settings, IAuthenticator auth)
    {
        _settings = settings;
        _auth = auth;
    }

    [Function("GetVisionModelSettings")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "vision-model-settings")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            EnsureAdmin(user);
            return HttpResults.Ok(await _settings.GetAsync(req.HttpContext.RequestAborted));
        });

    [Function("SetVisionModelOverride")]
    public Task<IActionResult> Set(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "vision-model-settings")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            EnsureAdmin(user);
            var input = await HttpResults.ReadJsonAsync<VisionModelOverrideInput>(req, req.HttpContext.RequestAborted);
            var updated = await _settings.SetOverrideAsync(input.Model, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("ClearVisionModelOverride")]
    public Task<IActionResult> Clear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "vision-model-settings")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            EnsureAdmin(user);
            await _settings.ClearOverrideAsync(req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });

    private static void EnsureAdmin(Domain.Entities.AppUser user)
    {
        if (!user.IsActingAsAdmin)
            throw new ForbiddenException("This action requires admin access.");
    }
}
