using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for the Recipes feature: CRUD plus the public-recipe review workflow
/// (submit for review, and the admin review queue with approve/reject).
/// </summary>
public sealed class RecipeFunctions
{
    private readonly RecipeService _recipes;
    private readonly IAuthenticator _auth;

    public RecipeFunctions(RecipeService recipes, IAuthenticator auth)
    {
        _recipes = recipes;
        _auth = auth;
    }

    [Function("ListRecipes")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var search = req.Query["search"].FirstOrDefault();
            // Admin "view all": honored only when the caller is acting as an admin (enforced in the service).
            var all = req.Query["all"].FirstOrDefault() == "true";
            var result = await _recipes.ListAsync(user, search, all, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    [Function("GetRecipeReviewQueue")]
    public Task<IActionResult> ReviewQueue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/review-queue")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _recipes.ListReviewQueueAsync(user, req.HttpContext.RequestAborted)));

    [Function("GetRecipe")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _recipes.GetAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("CreateRecipe")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<RecipeInput>(req, req.HttpContext.RequestAborted);
            var created = await _recipes.CreateAsync(user, input, req.HttpContext.RequestAborted);
            return HttpResults.Created(created);
        });

    [Function("UpdateRecipe")]
    public Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "recipes/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<RecipeInput>(req, req.HttpContext.RequestAborted);
            var updated = await _recipes.UpdateAsync(user, id, input, req.HttpContext.RequestAborted);
            return HttpResults.Ok(updated);
        });

    [Function("DeleteRecipe")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "recipes/{id}")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            await _recipes.DeleteAsync(user, id, req.HttpContext.RequestAborted);
            return HttpResults.NoContent();
        });

    [Function("SubmitRecipeForReview")]
    public Task<IActionResult> SubmitForReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/{id}/submit-for-review")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _recipes.SubmitForReviewAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("ApproveRecipe")]
    public Task<IActionResult> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/{id}/approve")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _recipes.ApproveAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("RejectRecipe")]
    public Task<IActionResult> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/{id}/reject")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _recipes.RejectAsync(user, id, req.HttpContext.RequestAborted)));
}
