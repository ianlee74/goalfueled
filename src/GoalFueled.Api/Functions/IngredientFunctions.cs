using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>HTTP endpoints for the fully-implemented Ingredients feature (the MVP vertical slice).</summary>
public sealed class IngredientFunctions
{
    private readonly IngredientService _ingredients;
    private readonly NutritionLookupService _nutritionLookup;
    private readonly IAuthenticator _auth;

    public IngredientFunctions(
        IngredientService ingredients, NutritionLookupService nutritionLookup, IAuthenticator auth)
    {
        _ingredients = ingredients;
        _nutritionLookup = nutritionLookup;
        _auth = auth;
    }

    [Function("ListIngredients")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var search = req.Query["search"].FirstOrDefault();
            // Admin "view all": honored only when the caller is acting as an admin (enforced in the service).
            var all = req.Query["all"].FirstOrDefault() == "true";
            var result = await _ingredients.ListAsync(user, search, all, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    [Function("LookupIngredientNutrition")]
    public Task<IActionResult> NutritionLookup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients/nutrition-lookup")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async _ =>
        {
            var query = req.Query["query"].FirstOrDefault();
            var result = await _nutritionLookup.LookupAsync(query, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    [Function("GetIngredient")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _ingredients.GetAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("CreateIngredient")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<IngredientInput>(req, req.HttpContext.RequestAborted);
            var created = await _ingredients.CreateAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(created);
        });

    [Function("CustomizeIngredient")]
    public Task<IActionResult> Customize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients/{id}/customize")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Created(await _ingredients.CustomizeAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("UpdateIngredient")]
    public Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ingredients/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<IngredientInput>(req, req.HttpContext.RequestAborted);
            var updated = await _ingredients.UpdateAsync(user, id, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("DeleteIngredient")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ingredients/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _ingredients.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });
}
