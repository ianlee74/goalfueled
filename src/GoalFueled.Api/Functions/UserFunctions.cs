using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>Endpoints for the current user's profile, onboarding, and admin-view toggle.</summary>
public sealed class UserFunctions
{
    private readonly IAuthenticator _auth;
    private readonly IUserRepository _users;

    public UserFunctions(IAuthenticator auth, IUserRepository users)
    {
        _auth = auth;
        _users = users;
    }

    /// <summary>Returns (and provisions on first call) the signed-in user's profile.</summary>
    [Function("GetMe")]
    public Task<IActionResult> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, user => Task.FromResult(HttpResults.Ok(ToDto(user))));

    /// <summary>Completes the first-login welcome flow by recording the user's first goal.</summary>
    [Function("CompleteOnboarding")]
    public Task<IActionResult> Onboarding(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "me/onboarding")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<OnboardingInput>(req, req.HttpContext.RequestAborted);
            if (!Enum.IsDefined(typeof(GoalType), input.PrimaryGoal))
                throw new ValidationException("Unknown goal type.");

            user.PrimaryGoal = input.PrimaryGoal;
            user.HasCompletedOnboarding = true;
            await _users.UpdateAsync(user, req.HttpContext.RequestAborted);
            return HttpResults.Ok(ToDto(user));
        });

    /// <summary>Toggles an admin's "admin view" on or off so they can browse as a regular user.</summary>
    [Function("SetAdminView")]
    public Task<IActionResult> AdminView(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "me/admin-view")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            if (!user.IsAdmin) throw new ForbiddenException("Only admins can change the admin view.");
            var input = await HttpResults.ReadJsonAsync<AdminViewInput>(req, req.HttpContext.RequestAborted);
            user.AdminViewEnabled = input.Enabled;
            await _users.UpdateAsync(user, req.HttpContext.RequestAborted);
            return HttpResults.Ok(ToDto(user));
        });

    private static CurrentUserDto ToDto(AppUser u) => new(
        u.Id, u.Email, u.DisplayName, u.AvatarUrl, u.Roles, u.IsAdmin,
        u.AdminViewEnabled, u.IsActingAsAdmin, u.PrimaryGoal, u.HasCompletedOnboarding);
}
