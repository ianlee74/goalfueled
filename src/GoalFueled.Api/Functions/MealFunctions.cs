using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for the Meals feature: CRUD over meals assembled from portions of prepared
/// recipes, each with its rolled-up nutrition summary.
/// </summary>
public sealed class MealFunctions
{
    private readonly MealService _meals;
    private readonly IAuthenticator _auth;

    public MealFunctions(MealService meals, IAuthenticator auth)
    {
        _meals = meals;
        _auth = auth;
    }

    [Function("ListMeals")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "meals")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var search = req.Query["search"].FirstOrDefault();
            // Admin "view all": honored only when the caller is acting as an admin (enforced in the service).
            var all = req.Query["all"].FirstOrDefault() == "true";
            var result = await _meals.ListAsync(user, search, all, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    [Function("GetMeal")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "meals/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _meals.GetAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("CreateMeal")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "meals")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<MealInput>(req, req.HttpContext.RequestAborted);
            var created = await _meals.CreateAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(created);
        });

    [Function("UpdateMeal")]
    public Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "meals/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<MealInput>(req, req.HttpContext.RequestAborted);
            var updated = await _meals.UpdateAsync(user, id, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("DeleteMeal")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "meals/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _meals.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
