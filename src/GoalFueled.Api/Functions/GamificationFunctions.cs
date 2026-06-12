using System.Globalization;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for gamification (DESIGN.md §4): the level/Fuel-Point summary, the weekly
/// "your week in fuel" review, and completing a review for its one-time bonus. All data is
/// scoped to the authenticated user.
/// </summary>
public sealed class GamificationFunctions
{
    private readonly GamificationService _gamification;
    private readonly IAuthenticator _auth;

    public GamificationFunctions(GamificationService gamification, IAuthenticator auth)
    {
        _gamification = gamification;
        _auth = auth;
    }

    [Function("GetGamificationSummary")]
    public Task<IActionResult> Summary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "gamification")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _gamification.GetSummaryAsync(user, null, req.HttpContext.RequestAborted)));

    [Function("GetWeeklyReview")]
    public Task<IActionResult> Review(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "gamification/review")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var raw = req.Query["weekStart"].FirstOrDefault();
            DateOnly? weekStart = !string.IsNullOrWhiteSpace(raw)
                && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            return HttpResults.Ok(await _gamification.GetReviewAsync(user, weekStart, null, req.HttpContext.RequestAborted));
        });

    [Function("CompleteWeeklyReview")]
    public Task<IActionResult> Complete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "gamification/review/complete")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<CompleteReviewInput>(req, req.HttpContext.RequestAborted);
            return HttpResults.Ok(await _gamification.CompleteReviewAsync(user, input, null, req.HttpContext.RequestAborted));
        });
}
