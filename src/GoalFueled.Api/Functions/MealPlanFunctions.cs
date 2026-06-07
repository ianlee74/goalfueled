using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for the Meal Plans feature: CRUD over personal plans of when to consume daily meals,
/// each with its daily/weekly nutrition summary for goal tracking.
/// </summary>
public sealed class MealPlanFunctions
{
    private readonly MealPlanService _plans;
    private readonly IAuthenticator _auth;

    public MealPlanFunctions(MealPlanService plans, IAuthenticator auth)
    {
        _plans = plans;
        _auth = auth;
    }

    [Function("ListMealPlans")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mealplans")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var search = req.Query["search"].FirstOrDefault();
            var result = await _plans.ListAsync(user, search, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    [Function("GetMealPlan")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mealplans/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _plans.GetAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("CreateMealPlan")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mealplans")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<MealPlanInput>(req, req.HttpContext.RequestAborted);
            var created = await _plans.CreateAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(created);
        });

    [Function("UpdateMealPlan")]
    public Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "mealplans/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<MealPlanInput>(req, req.HttpContext.RequestAborted);
            var updated = await _plans.UpdateAsync(user, id, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("DeleteMealPlan")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "mealplans/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _plans.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
