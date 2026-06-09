using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

public class ConsumptionServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(AppUser admin, AppUser user, AppUser other,
        IngredientService ingredients, RecipeService recipes, MealService meals,
        ConsumptionService consumption)> SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var ingredients = new IngredientService(new IngredientRepository(db));
        var recipes = new RecipeService(new RecipeRepository(db), new IngredientRepository(db));
        var meals = new MealService(new MealRepository(db), new RecipeRepository(db), new IngredientRepository(db));
        var consumption = new ConsumptionService(
            new ConsumptionEntryRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));

        return (admin!, user!, other!, ingredients, recipes, meals, consumption);
    }

    // Builds a recipe supplying exactly `calories` kcal per serving (1-serving recipe) owned by `user`.
    private static async Task<RecipeDto> BuildCaloricRecipeAsync(
        AppUser admin, AppUser user, IngredientService ingredients, RecipeService recipes, double calories)
    {
        var ing = await ingredients.CreateAsync(admin,
            new IngredientInput("kcal-base", null, null, UnitOfMeasure.Gram, 1,
                new List<NutrientAmount> { new("calories", 1) }, true), default);
        return await recipes.CreateAsync(user,
            new RecipeInput("kcal-recipe", null, null,
                new List<RecipeItemInput> { new(ing.Id, calories, UnitOfMeasure.Gram) }, 1, false), default);
    }

    private static double CaloriesOf(IReadOnlyList<NutrientAmount> n) =>
        n.FirstOrDefault(a => a.NutrientId == "calories")?.Value ?? 0;

    // ---- logging recipes/meals from the catalog ----

    [Fact]
    public async Task Log_recipe_records_entry_with_scaled_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, ingredients, recipes, _, consumption) = await SetupAsync(db);
        var recipe = await BuildCaloricRecipeAsync(admin, user, ingredients, recipes, 100);

        // Eat 2 servings → 200 kcal logged.
        var entry = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.Recipe, recipe.Id, Servings: 2), default);

        Assert.Equal(ConsumptionSource.Recipe, entry.Source);
        Assert.Equal(recipe.Id, entry.SourceId);
        Assert.Equal("kcal-recipe", entry.Name); // snapshotted from the catalog
        Assert.Equal(2, entry.Servings);
        Assert.Equal(200, CaloriesOf(entry.Nutrition), 1);
        Assert.False(entry.SourceMissing);
        Assert.Equal(Today, entry.Date); // defaults to today
    }

    [Fact]
    public async Task Log_meal_records_entry_with_scaled_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, ingredients, recipes, meals, consumption) = await SetupAsync(db);
        var recipe = await BuildCaloricRecipeAsync(admin, user, ingredients, recipes, 100);
        var meal = await meals.CreateAsync(user,
            new MealInput("kcal-meal", new List<MealPortionInput> { new(recipe.Id, 1) }, false), default);

        var entry = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.Meal, meal.Id, Servings: 1.5), default);

        Assert.Equal(ConsumptionSource.Meal, entry.Source);
        Assert.Equal("kcal-meal", entry.Name);
        Assert.Equal(150, CaloriesOf(entry.Nutrition), 1); // 100 kcal/meal × 1.5
    }

    [Fact]
    public async Task Log_free_form_item_with_inline_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);

        var entry = await consumption.LogAsync(user, new ConsumptionEntryInput(
            ConsumptionSource.FreeForm, Name: "Handful of chips", Servings: 2,
            Nutrition: new List<NutrientAmount> { new("calories", 150) }), default);

        Assert.Equal(ConsumptionSource.FreeForm, entry.Source);
        Assert.Null(entry.SourceId);
        Assert.Equal("Handful of chips", entry.Name);
        Assert.Equal(300, CaloriesOf(entry.Nutrition), 1); // 150 per serving × 2
    }

    [Fact]
    public async Task Log_free_form_item_without_nutrition_is_allowed()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);

        var entry = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "Mystery snack"), default);

        Assert.Equal("Mystery snack", entry.Name);
        Assert.Empty(entry.Nutrition);
    }

    // ---- day view & totals ----

    [Fact]
    public async Task Day_view_sums_nutrition_across_entries()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, ingredients, recipes, _, consumption) = await SetupAsync(db);
        var recipe = await BuildCaloricRecipeAsync(admin, user, ingredients, recipes, 100);

        await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.Recipe, recipe.Id, Servings: 2), default); // 200
        await consumption.LogAsync(user, new ConsumptionEntryInput(
            ConsumptionSource.FreeForm, Name: "Soda", Servings: 1,
            Nutrition: new List<NutrientAmount> { new("calories", 140) }), default); // 140

        var day = await consumption.GetDayAsync(user, Today, default);

        Assert.Equal(Today, day.Date);
        Assert.Equal(2, day.Entries.Count);
        Assert.Equal(340, CaloriesOf(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Entries_are_scoped_per_day()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);

        await consumption.LogAsync(user, new ConsumptionEntryInput(
            ConsumptionSource.FreeForm, Name: "Yesterday snack", Date: Today.AddDays(-1)), default);
        await consumption.LogAsync(user, new ConsumptionEntryInput(
            ConsumptionSource.FreeForm, Name: "Today snack"), default);

        var today = await consumption.GetDayAsync(user, Today, default);
        var yesterday = await consumption.GetDayAsync(user, Today.AddDays(-1), default);

        Assert.Single(today.Entries);
        Assert.Equal("Today snack", today.Entries[0].Name);
        Assert.Single(yesterday.Entries);
        Assert.Equal("Yesterday snack", yesterday.Entries[0].Name);
    }

    [Fact]
    public async Task Day_defaults_to_today_when_date_omitted()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);
        await consumption.LogAsync(user, new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "Snack"), default);

        var day = await consumption.GetDayAsync(user, date: null, default);
        Assert.Equal(Today, day.Date);
        Assert.Single(day.Entries);
    }

    // ---- removal ----

    [Fact]
    public async Task Remove_deletes_the_entry()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);
        var entry = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "Snack"), default);

        await consumption.RemoveAsync(user, entry.Id, default);

        var day = await consumption.GetDayAsync(user, Today, default);
        Assert.Empty(day.Entries);
    }

    // ---- per-user isolation ----

    [Fact]
    public async Task Users_cannot_see_or_remove_each_others_entries()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, other, _, _, _, consumption) = await SetupAsync(db);

        var mine = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "Mine"), default);
        await consumption.LogAsync(other,
            new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "Theirs"), default);

        Assert.Single((await consumption.GetDayAsync(user, Today, default)).Entries);
        Assert.Single((await consumption.GetDayAsync(other, Today, default)).Entries);
        // Other cannot remove mine (404, surfaced as NotFound).
        await Assert.ThrowsAsync<NotFoundException>(() => consumption.RemoveAsync(other, mine.Id, default));
    }

    [Fact]
    public async Task Cannot_log_recipe_owned_by_another_user()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, other, ingredients, recipes, _, consumption) = await SetupAsync(db);
        // A private recipe owned by `other`.
        var recipe = await BuildCaloricRecipeAsync(admin, other, ingredients, recipes, 100);

        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user,
                new ConsumptionEntryInput(ConsumptionSource.Recipe, recipe.Id, Servings: 1), default));
    }

    // ---- validation ----

    [Fact]
    public async Task Validation_rejects_missing_source_id_for_catalog_entry()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user, new ConsumptionEntryInput(ConsumptionSource.Recipe, SourceId: null), default));
    }

    [Fact]
    public async Task Validation_rejects_free_form_without_name()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user, new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "  "), default));
    }

    [Fact]
    public async Task Validation_rejects_non_positive_and_oversized_servings()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user, new ConsumptionEntryInput(ConsumptionSource.FreeForm, Name: "x", Servings: 0), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user, new ConsumptionEntryInput(
                ConsumptionSource.FreeForm, Name: "x", Servings: InputLimits.ConsumptionServingsMax + 1), default));
    }

    [Fact]
    public async Task Validation_rejects_future_date()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, consumption) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            consumption.LogAsync(user, new ConsumptionEntryInput(
                ConsumptionSource.FreeForm, Name: "x", Date: Today.AddDays(1)), default));
    }

    [Fact]
    public async Task Missing_catalog_source_marks_entry_source_missing()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, ingredients, recipes, _, consumption) = await SetupAsync(db);
        var recipe = await BuildCaloricRecipeAsync(admin, user, ingredients, recipes, 100);
        var entry = await consumption.LogAsync(user,
            new ConsumptionEntryInput(ConsumptionSource.Recipe, recipe.Id, Servings: 1), default);

        // Delete the underlying recipe; the logged entry should still list, name preserved, marked missing.
        await recipes.DeleteAsync(user, recipe.Id, default);

        var day = await consumption.GetDayAsync(user, Today, default);
        Assert.Single(day.Entries);
        Assert.True(day.Entries[0].SourceMissing);
        Assert.Equal("kcal-recipe", day.Entries[0].Name);
        Assert.Empty(day.Entries[0].Nutrition);
    }
}
