using System.Globalization;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoint for the landing-page dashboard: a single aggregated payload (next upcoming meals, the
/// "weight due today" prompt, and the 7-day fuel/burn/kindle/net series with the estimated weight change).
/// All data is scoped to the authenticated user; the aggregation lives in <see cref="DashboardService"/>.
/// </summary>
public sealed class DashboardFunctions
{
    private readonly DashboardService _dashboard;
    private readonly IAuthenticator _auth;

    public DashboardFunctions(DashboardService dashboard, IAuthenticator auth)
    {
        _dashboard = dashboard;
        _auth = auth;
    }

    [Function("GetDashboard")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            // Optional ?date=yyyy-MM-dd (defaults to today server-side when omitted/blank).
            var raw = req.Query["date"].FirstOrDefault();
            DateOnly? date = !string.IsNullOrWhiteSpace(raw)
                && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            return HttpResults.Ok(await _dashboard.GetAsync(user, date, now: null, req.HttpContext.RequestAborted));
        });
}
