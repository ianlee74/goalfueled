using GoalFueled.Api.Common;
using GoalFueled.Domain.Nutrition;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>Exposes the nutrient catalog (reference data) so the UI can render label-style editors.</summary>
public sealed class NutrientFunctions
{
    [Function("ListNutrients")]
    public IActionResult List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nutrients")] HttpRequest req) =>
        HttpResults.Ok(NutrientCatalog.All);
}
