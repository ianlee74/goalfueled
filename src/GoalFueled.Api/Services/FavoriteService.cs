using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for marking ingredients, recipes, and meals as the caller's favorites.
/// Favorites are PER USER and stored on the <see cref="AppUser"/> document (id lists) because the
/// underlying items can be app-level/shared — a flag on the entity itself would leak one user's
/// favorite to everyone. Favoriting requires the item to exist and be visible to the caller;
/// un-favoriting always succeeds so stale ids can be cleaned up even after an item is deleted.
/// </summary>
public sealed class FavoriteService
{
    /// <summary>Generous ceiling on each favorites list so a runaway client can't bloat the user document.</summary>
    public const int FavoritesMax = 1_000;

    private readonly IUserRepository _users;
    private readonly IIngredientRepository _ingredients;
    private readonly IRecipeRepository _recipes;
    private readonly IMealRepository _meals;

    public FavoriteService(
        IUserRepository users,
        IIngredientRepository ingredients,
        IRecipeRepository recipes,
        IMealRepository meals)
    {
        _users = users;
        _ingredients = ingredients;
        _recipes = recipes;
        _meals = meals;
    }

    /// <summary>Marks/unmarks an ingredient as one of the caller's favorites.</summary>
    public async Task<FavoriteStateDto> SetIngredientFavoriteAsync(
        AppUser user, string id, bool favorite, CancellationToken ct)
    {
        if (favorite)
        {
            var ingredient = await _ingredients.GetByIdAsync(id, ct);
            if (ingredient is null || !CanView(ingredient, user))
                throw new NotFoundException("Ingredient not found.");
        }

        user.FavoriteIngredientIds = Apply(user.FavoriteIngredientIds, id, favorite, "ingredients");
        await _users.UpdateAsync(user, ct);
        return new FavoriteStateDto(id, favorite);
    }

    /// <summary>Marks/unmarks a recipe as one of the caller's favorites.</summary>
    public async Task<FavoriteStateDto> SetRecipeFavoriteAsync(
        AppUser user, string id, bool favorite, CancellationToken ct)
    {
        if (favorite)
        {
            var recipe = await _recipes.GetByIdAsync(id, ct);
            if (recipe is null || !CanView(recipe, user))
                throw new NotFoundException("Recipe not found.");
        }

        user.FavoriteRecipeIds = Apply(user.FavoriteRecipeIds, id, favorite, "recipes");
        await _users.UpdateAsync(user, ct);
        return new FavoriteStateDto(id, favorite);
    }

    /// <summary>Marks/unmarks a meal as one of the caller's favorites.</summary>
    public async Task<FavoriteStateDto> SetMealFavoriteAsync(
        AppUser user, string id, bool favorite, CancellationToken ct)
    {
        if (favorite)
        {
            var meal = await _meals.GetByIdAsync(id, ct);
            if (meal is null || !CanView(meal, user))
                throw new NotFoundException("Meal not found.");
        }

        user.FavoriteMealIds = Apply(user.FavoriteMealIds, id, favorite, "meals");
        await _users.UpdateAsync(user, ct);
        return new FavoriteStateDto(id, favorite);
    }

    // ---- visibility (mirrors each entity service's EnsureCanView) ----

    private static bool CanView(Ingredient i, AppUser user) =>
        i.Scope == OwnershipScope.App || i.OwnerUserId == user.Id || user.IsActingAsAdmin;

    private static bool CanView(Recipe r, AppUser user) =>
        r.Scope == OwnershipScope.App || r.Visibility == RecipeVisibility.Public ||
        r.OwnerUserId == user.Id || user.IsActingAsAdmin;

    private static bool CanView(Meal m, AppUser user) =>
        m.Scope == OwnershipScope.App || m.OwnerUserId == user.Id || user.IsActingAsAdmin;

    /// <summary>
    /// Returns the list with <paramref name="id"/> added or removed. A NEW list is always returned
    /// (never an in-place mutation) so EF's change tracking reliably sees the update on both the
    /// in-memory and Cosmos providers; an emptied list collapses back to null so user documents
    /// without favorites stay free of the property.
    /// </summary>
    private static List<string>? Apply(List<string>? ids, string id, bool favorite, string label)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ValidationException("An item id is required.");

        var result = ids is null ? new List<string>() : new List<string>(ids);
        if (favorite)
        {
            if (!result.Contains(id))
            {
                InputLimits.LimitCount(result.Count + 1, FavoritesMax, $"favorite {label}");
                result.Add(id);
            }
        }
        else
        {
            result.Remove(id);
        }

        return result.Count == 0 ? null : result;
    }
}
