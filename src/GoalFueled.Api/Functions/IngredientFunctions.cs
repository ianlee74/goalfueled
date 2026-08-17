using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Api.Services.OpenFoodFacts;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>HTTP endpoints for the fully-implemented Ingredients feature (the MVP vertical slice).</summary>
public sealed class IngredientFunctions
{
    private readonly IngredientService _ingredients;
    private readonly NutritionLookupService _nutritionLookup;
    private readonly BarcodeLookupService _barcodeLookup;
    private readonly IAuthenticator _auth;

    public IngredientFunctions(
        IngredientService ingredients, NutritionLookupService nutritionLookup,
        BarcodeLookupService barcodeLookup, IAuthenticator auth)
    {
        _ingredients = ingredients;
        _nutritionLookup = nutritionLookup;
        _barcodeLookup = barcodeLookup;
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

    // NOTE: this is a distinct top-level route, NOT "ingredients/nutrition-lookup". In the Functions
    // isolated host the literal segment does not reliably win over the "ingredients/{id}" route, so a
    // GET to "ingredients/nutrition-lookup" was being captured by GetIngredient (id="nutrition-lookup")
    // and answered with 404 "Ingredient not found." — the lookup handler never ran. A non-colliding
    // path makes the route unambiguous so it can never be shadowed.
    [Function("LookupIngredientNutrition")]
    public Task<IActionResult> NutritionLookup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredient-nutrition-lookup")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var query = req.Query["query"].FirstOrDefault();
            var result = await _nutritionLookup.LookupAsync(user.Id, query, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    // Barcode lookup for the scan flow. Distinct top-level route (NOT "ingredients/...") so it can't be
    // shadowed by the "ingredients/{id}" route — the same route-shadowing class as the lookup above.
    [Function("LookupIngredientByBarcode")]
    public Task<IActionResult> BarcodeLookup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredient-by-barcode")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            // Feature-tagged capability: barcode scanning must be enabled for this account.
            FeatureGuard.EnsureFeature(user, FeatureKeys.BarcodeScanning);
            var barcode = req.Query["barcode"].FirstOrDefault();
            var result = await _barcodeLookup.LookupAsync(user.Id, barcode, req.HttpContext.RequestAborted);
            return HttpResults.Ok(result);
        });

    // Distinct top-level route, NOT "ingredients/review-queue": in the Functions isolated host the
    // literal "review-queue" segment is captured by "ingredients/{id}" (id="review-queue") and answered
    // with 404 "Ingredient not found." See LookupIngredientNutrition above for the same shadowing class.
    [Function("GetIngredientReviewQueue")]
    public Task<IActionResult> ReviewQueue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredient-review-queue")] HttpRequest req) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _ingredients.ListReviewQueueAsync(user, req.HttpContext.RequestAborted)));

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

    [Function("SubmitIngredientForReview")]
    public Task<IActionResult> SubmitForReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients/{id}/submit-for-review")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _ingredients.SubmitForReviewAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("ApproveIngredient")]
    public Task<IActionResult> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients/{id}/approve")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _ingredients.ApproveAsync(user, id, req.HttpContext.RequestAborted)));

    [Function("RejectIngredient")]
    public Task<IActionResult> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients/{id}/reject")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
            HttpResults.Ok(await _ingredients.RejectAsync(user, id, req.HttpContext.RequestAborted)));
}
