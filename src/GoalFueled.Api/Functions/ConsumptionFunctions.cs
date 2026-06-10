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
/// HTTP endpoints for the daily consumption log: view a day's logged items (with nutrition totals),
/// log an ad-hoc consumed item, and remove one. All data is scoped to the authenticated user.
/// </summary>
public sealed class ConsumptionFunctions
{
    private readonly ConsumptionService _consumption;
    private readonly IAuthenticator _auth;

    public ConsumptionFunctions(ConsumptionService consumption, IAuthenticator auth)
    {
        _consumption = consumption;
        _auth = auth;
    }

    [Function("GetConsumptionDay")]
    public Task<IActionResult> GetDay(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "consumption")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            // Optional ?date=yyyy-MM-dd (defaults to today server-side when omitted/blank).
            var raw = req.Query["date"].FirstOrDefault();
            DateOnly? date = !string.IsNullOrWhiteSpace(raw)
                && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            return HttpResults.Ok(await _consumption.GetDayAsync(user, date, req.HttpContext.RequestAborted));
        });

    [Function("LogConsumptionEntry")]
    public Task<IActionResult> Log(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consumption")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<ConsumptionEntryInput>(req, req.HttpContext.RequestAborted);
            var saved = await _consumption.LogAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(saved);
        });

    [Function("GetPlannedDay")]
    public Task<IActionResult> GetPlannedDay(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "consumption/planned")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var raw = req.Query["date"].FirstOrDefault();
            DateOnly? date = !string.IsNullOrWhiteSpace(raw)
                && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            return HttpResults.Ok(await _consumption.GetPlannedDayAsync(user, date, req.HttpContext.RequestAborted));
        });

    [Function("CompletePlannedMeal")]
    public Task<IActionResult> CompletePlannedMeal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consumption/planned/complete")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<CompletePlannedMealInput>(req, req.HttpContext.RequestAborted);
            var saved = await _consumption.CompletePlannedMealAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(saved);
        });

    [Function("UncompletePlannedMeal")]
    public Task<IActionResult> UncompletePlannedMeal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "consumption/planned/uncomplete")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<CompletePlannedMealInput>(req, req.HttpContext.RequestAborted);
            await _consumption.UncompletePlannedMealAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });

    [Function("DeleteConsumptionEntry")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "consumption/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _consumption.RemoveAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
