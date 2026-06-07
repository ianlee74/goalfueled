using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for weight tracking: list the caller's weight history and record a dated weight
/// (one entry per day, upserted). All data is scoped to the authenticated user.
/// </summary>
public sealed class WeightFunctions
{
    private readonly WeightService _weights;
    private readonly IAuthenticator _auth;

    public WeightFunctions(WeightService weights, IAuthenticator auth)
    {
        _weights = weights;
        _auth = auth;
    }

    [Function("ListWeightEntries")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "weights")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _weights.ListAsync(user, req.HttpContext.RequestAborted)));

    [Function("RecordWeightEntry")]
    public Task<IActionResult> Record(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "weights")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<WeightEntryInput>(req, req.HttpContext.RequestAborted);
            var saved = await _weights.RecordAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(saved);
        });
}
