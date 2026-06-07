using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>HTTP endpoints for the fully-implemented Ingredients feature (the MVP vertical slice).</summary>
public sealed class IngredientFunctions
{
    private readonly IngredientService _ingredients;
    private readonly IAuthenticator _auth;

    public IngredientFunctions(IngredientService ingredients, IAuthenticator auth)
    {
        _ingredients = ingredients;
        _auth = auth;
    }

    [Function("ListIngredients")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var search = req.Query["search"].FirstOrDefault();
            var result = await _ingredients.ListAsync(user, search, req.HttpContext.RequestAborted);
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
