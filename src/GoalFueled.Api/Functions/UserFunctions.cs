using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data.Repositories;
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
    private readonly UserProfileService _profile;

    public UserFunctions(IAuthenticator auth, IUserRepository users, UserProfileService profile)
    {
        _auth = auth;
        _users = users;
        _profile = profile;
    }

    /// <summary>Returns (and provisions on first call) the signed-in user's profile.</summary>
    [Function("GetMe")]
    public Task<IActionResult> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, user => Task.FromResult(HttpResults.Ok(ToDto(user))));

    /// <summary>
    /// Completes the first-login welcome flow, capturing the user's basic info (goal, sex at birth,
    /// height) and recording their current weight as today's entry.
    /// </summary>
    [Function("CompleteOnboarding")]
    public Task<IActionResult> Onboarding(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "me/onboarding")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<OnboardingInput>(req, req.HttpContext.RequestAborted);
            var updated = await _profile.CompleteOnboardingAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(ToDto(updated));
        });

    /// <summary>Updates the editable profile attributes later (sex, height, goal, optional current weight).</summary>
    [Function("UpdateProfile")]
    public Task<IActionResult> UpdateProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "me/profile")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<ProfileInput>(req, req.HttpContext.RequestAborted);
            var updated = await _profile.UpdateProfileAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(ToDto(updated));
        });

    /// <summary>Chooses (or clears) the meal plan the user is currently following (drives The Log).</summary>
    [Function("SetActiveMealPlan")]
    public Task<IActionResult> SetActiveMealPlan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "me/active-plan")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<ActiveMealPlanInput>(req, req.HttpContext.RequestAborted);
            var updated = await _profile.SetActiveMealPlanAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(ToDto(updated));
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
        u.AdminViewEnabled, u.IsActingAsAdmin, u.PrimaryGoal, u.SexAtBirth, u.HeightCm,
        u.HasCompletedOnboarding, u.ActiveMealPlanId);
}
