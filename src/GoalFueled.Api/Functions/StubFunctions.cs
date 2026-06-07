using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// Placeholder endpoints for features scaffolded but not yet implemented in this MVP slice
/// (Meals, Meal Plans). They authenticate like real endpoints and return an empty,
/// "coming soon" payload so the frontend can render its stub screens against a live API.
/// Replace each with a full feature service following the Ingredients/Recipes pattern.
/// </summary>
public sealed class StubFunctions
{
    private readonly IAuthenticator _auth;

    public StubFunctions(IAuthenticator auth) => _auth = auth;

    [Function("ListMeals")]
    public Task<IActionResult> Meals(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "meals")] HttpRequest req) =>
        ComingSoon(req, "meals");

    [Function("ListMealPlans")]
    public Task<IActionResult> MealPlans(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mealplans")] HttpRequest req) =>
        ComingSoon(req, "mealPlans");

    private Task<IActionResult> ComingSoon(HttpRequest req, string feature) =>
        ApiEndpoint.RunAuthenticated(req, _auth, _ =>
            Task.FromResult(HttpResults.Ok(new { feature, status = "coming-soon", items = Array.Empty<object>() })));
}
