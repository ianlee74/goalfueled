using GoalFueled.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>Unauthenticated liveness probe.</summary>
public sealed class HealthFunctions
{
    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req) =>
        HttpResults.Ok(new { status = "ok", service = "goalfueled-api" });
}
