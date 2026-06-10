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
/// HTTP endpoints for the daily burn log: view a day's logged activities (with the day's total calories
/// burned), log an activity, list past activity-name suggestions for autocomplete, and remove an activity.
/// All data is scoped to the authenticated user.
/// </summary>
public sealed class BurnFunctions
{
    private readonly BurnService _burn;
    private readonly IAuthenticator _auth;

    public BurnFunctions(BurnService burn, IAuthenticator auth)
    {
        _burn = burn;
        _auth = auth;
    }

    [Function("GetBurnDay")]
    public Task<IActionResult> GetDay(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "burn")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            // Optional ?date=yyyy-MM-dd (defaults to today server-side when omitted/blank).
            var raw = req.Query["date"].FirstOrDefault();
            DateOnly? date = !string.IsNullOrWhiteSpace(raw)
                && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            return HttpResults.Ok(await _burn.GetDayAsync(user, date, req.HttpContext.RequestAborted));
        });

    [Function("LogBurnActivity")]
    public Task<IActionResult> Log(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "burn")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<BurnActivityInput>(req, req.HttpContext.RequestAborted);
            var saved = await _burn.LogAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(saved);
        });

    [Function("GetBurnSuggestions")]
    public Task<IActionResult> GetSuggestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "burn/suggestions")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _burn.GetSuggestionsAsync(user, req.HttpContext.RequestAborted)));

    [Function("DeleteBurnActivity")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "burn/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _burn.RemoveAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
