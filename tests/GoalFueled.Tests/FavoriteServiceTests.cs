using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

/// <summary>
/// Favorites: per-user marks on ingredients/recipes/meals, stored on the user document (the
/// underlying items can be app-level/shared, so the mark must never live on the entity itself).
/// </summary>
public class FavoriteServiceTests
{
    // Provisions an admin (first) and a regular user, plus all entity services and the favorite
    // service against the same context.
    private static async Task<(AppUser admin, AppUser user,
        IngredientService ingredients, RecipeService recipes, MealService meals, FavoriteService favorites)>
        SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var ingredients = new IngredientService(new IngredientRepository(db));
        var recipes = new RecipeService(new RecipeRepository(db), new IngredientRepository(db));
        var meals = new MealService(new MealRepository(db), new RecipeRepository(db), new IngredientRepository(db));
        var favorites = new FavoriteService(
            new UserRepository(db), new IngredientRepository(db), new RecipeRepository(db), new MealRepository(db));
        return (admin!, user!, ingredients, recipes, meals, favorites);
    }

    private static IngredientInput Ingredient(string name, bool appLevel = false) =>
        new(name, null, null, GoalFueled.Domain.UnitOfMeasure.Gram, 100, null, appLevel);

    [Fact]
    public async Task Favoriting_an_ingredient_marks_it_for_that_user_only()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, _, _, favorites) = await SetupAsync(db);

        // An app-level (shared) ingredient: the favorite must be per-user, not on the entity.
        var salt = await ingredients.CreateAsync(admin, Ingredient("Salt", appLevel: true), default);

        var state = await favorites.SetIngredientFavoriteAsync(user, salt.Id, true, default);
        Assert.True(state.IsFavorite);

        var asUser = Assert.Single(await ingredients.ListAsync(user, null, ct: default), i => i.Id == salt.Id);
        Assert.True(asUser.IsFavorite);

        // The other user (the admin) sees the same shared item unmarked.
        var asAdmin = Assert.Single(await ingredients.ListAsync(admin, null, ct: default), i => i.Id == salt.Id);
        Assert.False(asAdmin.IsFavorite);
    }

    [Fact]
    public async Task Favorites_sort_to_the_top_with_name_order_within_groups()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, _, _, favorites) = await SetupAsync(db);

        await ingredients.CreateAsync(user, Ingredient("Apple"), default);
        var rice = await ingredients.CreateAsync(user, Ingredient("Rice"), default);
        var zucchini = await ingredients.CreateAsync(user, Ingredient("Zucchini"), default);

        await favorites.SetIngredientFavoriteAsync(user, zucchini.Id, true, default);
        await favorites.SetIngredientFavoriteAsync(user, rice.Id, true, default);

        var names = (await ingredients.ListAsync(user, null, ct: default)).Select(i => i.Name).ToList();
        // Favorites first (name-ordered), then the rest (name-ordered).
        Assert.Equal(new[] { "Rice", "Zucchini", "Apple" }, names);
    }

    [Fact]
    public async Task Unfavoriting_removes_the_mark()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, _, _, favorites) = await SetupAsync(db);

        var oats = await ingredients.CreateAsync(user, Ingredient("Oats"), default);
        await favorites.SetIngredientFavoriteAsync(user, oats.Id, true, default);

        var state = await favorites.SetIngredientFavoriteAsync(user, oats.Id, false, default);
        Assert.False(state.IsFavorite);

        var listed = Assert.Single(await ingredients.ListAsync(user, null, ct: default), i => i.Id == oats.Id);
        Assert.False(listed.IsFavorite);
        // The emptied list collapses back to null so old documents stay shape-stable.
        Assert.Null(user.FavoriteIngredientIds);
    }

    [Fact]
    public async Task Favoriting_an_item_that_is_missing_or_not_visible_throws_not_found()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, _, _, favorites) = await SetupAsync(db);

        // Unknown id.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            favorites.SetIngredientFavoriteAsync(user, "missing", true, default));

        // Another user's private ingredient is invisible — favoriting it must not leak its existence.
        var adminPrivate = await ingredients.CreateAsync(admin, Ingredient("Admin Private"), default);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            favorites.SetIngredientFavoriteAsync(user, adminPrivate.Id, true, default));
    }

    [Fact]
    public async Task Unfavoriting_a_deleted_item_succeeds_so_stale_ids_can_be_cleaned_up()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, _, _, favorites) = await SetupAsync(db);

        var oats = await ingredients.CreateAsync(user, Ingredient("Oats"), default);
        await favorites.SetIngredientFavoriteAsync(user, oats.Id, true, default);
        await ingredients.DeleteAsync(user, oats.Id, default);

        var state = await favorites.SetIngredientFavoriteAsync(user, oats.Id, false, default);
        Assert.False(state.IsFavorite);
        Assert.Null(user.FavoriteIngredientIds);
    }

    [Fact]
    public async Task Favoriting_is_idempotent()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, _, _, favorites) = await SetupAsync(db);

        var oats = await ingredients.CreateAsync(user, Ingredient("Oats"), default);
        await favorites.SetIngredientFavoriteAsync(user, oats.Id, true, default);
        await favorites.SetIngredientFavoriteAsync(user, oats.Id, true, default);

        Assert.Equal(new[] { oats.Id }, user.FavoriteIngredientIds);
    }

    [Fact]
    public async Task Recipe_favorites_mark_and_float_to_the_top()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes, _, favorites) = await SetupAsync(db);

        await recipes.CreateAsync(user, new RecipeInput("Apple Pie", null, null, null, 1), default);
        var stew = await recipes.CreateAsync(user, new RecipeInput("Stew", null, null, null, 1), default);

        await favorites.SetRecipeFavoriteAsync(user, stew.Id, true, default);

        var listed = await recipes.ListAsync(user, null, ct: default);
        Assert.Equal(new[] { "Stew", "Apple Pie" }, listed.Select(r => r.Name).ToList());
        Assert.True(listed[0].IsFavorite);
        Assert.False(listed[1].IsFavorite);
    }

    [Fact]
    public async Task Meal_favorites_mark_and_float_to_the_top()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals, favorites) = await SetupAsync(db);

        await meals.CreateAsync(user, new MealInput("Breakfast", null), default);
        var supper = await meals.CreateAsync(user, new MealInput("Supper", null), default);

        await favorites.SetMealFavoriteAsync(user, supper.Id, true, default);

        var listed = await meals.ListAsync(user, null, ct: default);
        Assert.Equal(new[] { "Supper", "Breakfast" }, listed.Select(m => m.Name).ToList());
        Assert.True(listed[0].IsFavorite);
        Assert.False(listed[1].IsFavorite);
    }

    [Fact]
    public async Task Favorites_persist_on_the_user_document()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, _, _, favorites) = await SetupAsync(db);

        var oats = await ingredients.CreateAsync(user, Ingredient("Oats"), default);
        await favorites.SetIngredientFavoriteAsync(user, oats.Id, true, default);

        // A fresh load of the user (as a new request would do) still carries the favorite.
        var reloaded = await new UserRepository(db).GetByIdAsync(user.Id, default);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.IsFavoriteIngredient(oats.Id));
    }
}
