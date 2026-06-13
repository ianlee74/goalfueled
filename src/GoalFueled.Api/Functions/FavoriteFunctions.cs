using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Domain.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GoalFueled.Api.Functions;

/// <summary>
/// HTTP endpoints for marking ingredients, recipes, and meals as the caller's favorites.
/// Favorites are per-user (stored on the user profile), so these live beside the entity routes
/// but only ever mutate the calling user's document.
/// </summary>
public sealed class FavoriteFunctions
{
    private readonly FavoriteService _favorites;
    private readonly IAuthenticator _auth;

    public FavoriteFunctions(FavoriteService favorites, IAuthenticator auth)
    {
        _favorites = favorites;
        _auth = auth;
    }

    [Function("SetIngredientFavorite")]
    public Task<IActionResult> SetIngredientFavorite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ingredients/{id}/favorite")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<FavoriteInput>(req, req.HttpContext.RequestAborted);
            var state = await _favorites.SetIngredientFavoriteAsync(user, id, input.IsFavorite, req.HttpContext.RequestAborted);
            return HttpResults.Ok(state);
        });

    [Function("SetRecipeFavorite")]
    public Task<IActionResult> SetRecipeFavorite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "recipes/{id}/favorite")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<FavoriteInput>(req, req.HttpContext.RequestAborted);
            var state = await _favorites.SetRecipeFavoriteAsync(user, id, input.IsFavorite, req.HttpContext.RequestAborted);
            return HttpResults.Ok(state);
        });

    [Function("SetMealFavorite")]
    public Task<IActionResult> SetMealFavorite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "meals/{id}/favorite")] HttpRequest req,
        string id) =>
        ApiEndpoint.RunAuthenticated(req, _auth, async user =>
        {
            var input = await HttpResults.ReadJsonAsync<FavoriteInput>(req, req.HttpContext.RequestAborted);
            var state = await _favorites.SetMealFavoriteAsync(user, id, input.IsFavorite, req.HttpContext.RequestAborted);
            return HttpResults.Ok(state);
        });
}
