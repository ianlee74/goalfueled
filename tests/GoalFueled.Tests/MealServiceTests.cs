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

public class MealServiceTests
{
    // Provisions an admin (first) and a regular user, plus the ingredient/recipe/meal services
    // against the same context.
    private static async Task<(AppUser admin, AppUser user, IngredientService ingredients, RecipeService recipes, MealService meals)>
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
        return (admin!, user!, ingredients, recipes, meals);
    }

    private static IngredientInput Ingredient(
        string name, UnitOfMeasure unit, double serving, bool appLevel, params (string id, double v)[] nutrients) =>
        new(name, null, null, unit, serving, nutrients.Select(n => new NutrientAmount(n.id, n.v)).ToList(), appLevel);

    private static RecipeInput Recipe(string name, double servings, bool appLevel, params RecipeItemInput[] items) =>
        new(name, null, null, items.ToList(), servings, appLevel);

    private static MealInput Meal(string name, bool appLevel, params MealPortionInput[] portions) =>
        new(name, portions.ToList(), appLevel);

    private static MealInput Meal(string name, double servings, bool appLevel, params MealPortionInput[] portions) =>
        new(name, portions.ToList(), appLevel, servings);

    private static double Nutrient(IReadOnlyList<NutrientAmount> amounts, string id) =>
        amounts.SingleOrDefault(a => a.NutrientId == id)?.Value ?? 0;

    // ---- RBAC ----

    [Fact]
    public async Task User_creates_meal_owned_by_them()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        var created = await meals.CreateAsync(user, Meal("Lunch", false), default);

        Assert.Equal(OwnershipScope.User, created.Scope);
        Assert.Equal(user.Id, created.OwnerUserId);
        Assert.True(created.CanEdit);
    }

    [Fact]
    public async Task Admin_creates_app_level_meal_visible_to_everyone()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, meals) = await SetupAsync(db);

        await meals.CreateAsync(admin, Meal("Cafeteria Special", appLevel: true), default);

        var asUser = await meals.ListAsync(user, null, default);
        var special = Assert.Single(asUser, m => m.Name == "Cafeteria Special");
        Assert.Equal(OwnershipScope.App, special.Scope);
        Assert.False(special.CanEdit);
    }

    [Fact]
    public async Task AppLevel_flag_is_ignored_for_non_admins()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        var created = await meals.CreateAsync(user, Meal("Pretend App", appLevel: true), default);

        Assert.Equal(OwnershipScope.User, created.Scope);
        Assert.Equal(user.Id, created.OwnerUserId);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_meals()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await meals.CreateAsync(user, Meal("Secret", false), default);

        var otherList = await meals.ListAsync(other!, null, default);
        Assert.DoesNotContain(otherList, m => m.Id == mine.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => meals.GetAsync(other!, mine.Id, default));
    }

    [Fact]
    public async Task Owner_can_update_but_other_user_cannot()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await meals.CreateAsync(user, Meal("Mine", false), default);

        var updated = await meals.UpdateAsync(user, mine.Id, Meal("Mine v2", false), default);
        Assert.Equal("Mine v2", updated.Name);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            meals.UpdateAsync(other!, mine.Id, Meal("Hijacked", false), default));
    }

    [Fact]
    public async Task Admin_can_edit_and_delete_another_users_meal()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, meals) = await SetupAsync(db);

        var mine = await meals.CreateAsync(user, Meal("User Meal", false), default);

        var updated = await meals.UpdateAsync(admin, mine.Id, Meal("Edited by admin", false), default);
        Assert.Equal("Edited by admin", updated.Name);

        await meals.DeleteAsync(admin, mine.Id, default);
        await Assert.ThrowsAsync<NotFoundException>(() => meals.GetAsync(admin, mine.Id, default));
    }

    [Fact]
    public async Task Validation_rejects_empty_name_zero_servings_and_unknown_recipe()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes, meals) = await SetupAsync(db);
        var recipe = await recipes.CreateAsync(user, Recipe("Bowl", 1, false), default);

        await Assert.ThrowsAsync<ValidationException>(() => meals.CreateAsync(user, Meal("", false), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, Meal("Bad", false, new MealPortionInput(recipe.Id, 0)), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, Meal("Ghost", false, new MealPortionInput("does-not-exist", 1)), default));
    }

    [Fact]
    public async Task Cannot_reference_another_users_private_recipe()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes, meals) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var theirs = await recipes.CreateAsync(other!, Recipe("Theirs", 1, false), default);

        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, Meal("Steal", false, new MealPortionInput(theirs.Id, 1)), default));
    }

    // ---- portion nutrition math ----

    [Fact]
    public async Task Meal_nutrition_sums_portion_servings_times_recipe_per_serving()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);

        // 100 g chicken = 165 kcal, 31 g protein.
        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165), ("protein", 31)), default);

        // Recipe yields 2 servings, total = 200 g chicken (2 ingredient servings) → 330 kcal, 62 g protein.
        // Per recipe-serving = 165 kcal, 31 g protein.
        var dish = await recipes.CreateAsync(user, Recipe("Dish", 2, false,
            new RecipeItemInput(chicken.Id, 200, UnitOfMeasure.Gram)), default);

        // Meal = 1.5 servings of the dish → 1.5 × 165 = 247.5 kcal, 1.5 × 31 = 46.5 g protein.
        var meal = await meals.CreateAsync(user, Meal("Plate", false,
            new MealPortionInput(dish.Id, 1.5)), default);

        Assert.Equal(247.5, Nutrient(meal.TotalNutrition, "calories"), 3);
        Assert.Equal(46.5, Nutrient(meal.TotalNutrition, "protein"), 3);
    }

    [Fact]
    public async Task Per_serving_nutrition_is_total_divided_by_meal_servings()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);

        // 100 g chicken = 165 kcal, 31 g protein. Recipe yields 1 serving from 100 g.
        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165), ("protein", 31)), default);
        var dish = await recipes.CreateAsync(user, Recipe("Dish", 1, false,
            new RecipeItemInput(chicken.Id, 100, UnitOfMeasure.Gram)), default);

        // Meal = 4 servings of the dish, but the meal itself yields 2 servings.
        // Total: 4 × 165 = 660 kcal, 4 × 31 = 124 g protein. Per serving = total / 2.
        var meal = await meals.CreateAsync(user, Meal("Batch", servings: 2, appLevel: false,
            new MealPortionInput(dish.Id, 4)), default);

        Assert.Equal(2, meal.Servings, 3);
        Assert.Equal(660, Nutrient(meal.TotalNutrition, "calories"), 3);
        Assert.Equal(124, Nutrient(meal.TotalNutrition, "protein"), 3);
        Assert.Equal(330, Nutrient(meal.NutritionPerServing, "calories"), 3);
        Assert.Equal(62, Nutrient(meal.NutritionPerServing, "protein"), 3);
    }

    [Fact]
    public async Task Per_serving_defaults_to_total_when_meal_has_one_serving()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);

        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165)), default);
        var dish = await recipes.CreateAsync(user, Recipe("Dish", 1, false,
            new RecipeItemInput(chicken.Id, 100, UnitOfMeasure.Gram)), default);

        // Default-servings overload (legacy callers): meal yields 1 serving → per-serving == total.
        var meal = await meals.CreateAsync(user, Meal("Plate", false,
            new MealPortionInput(dish.Id, 2)), default);

        Assert.Equal(1, meal.Servings, 3);
        Assert.Equal(330, Nutrient(meal.TotalNutrition, "calories"), 3);
        Assert.Equal(330, Nutrient(meal.NutritionPerServing, "calories"), 3);
    }

    [Fact]
    public async Task Validation_rejects_zero_or_negative_meal_servings()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, Meal("Zero", servings: 0, appLevel: false), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, Meal("Negative", servings: -1, appLevel: false), default));
    }

    [Fact]
    public async Task Meal_nutrition_sums_across_multiple_recipe_portions()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);

        // 100 g rice = 130 kcal. Recipe yields 1 serving from 100 g → 130 kcal per serving.
        var rice = await ingredients.CreateAsync(admin,
            Ingredient("Rice", UnitOfMeasure.Gram, 100, true, ("calories", 130)), default);
        var riceDish = await recipes.CreateAsync(user, Recipe("Rice", 1, false,
            new RecipeItemInput(rice.Id, 100, UnitOfMeasure.Gram)), default);

        // 1 tbsp oil = 119 kcal. Recipe yields 2 servings from 2 tbsp → 119 kcal per serving.
        var oil = await ingredients.CreateAsync(admin,
            Ingredient("Olive Oil", UnitOfMeasure.Tablespoon, 1, true, ("calories", 119)), default);
        var oilDish = await recipes.CreateAsync(user, Recipe("Oil", 2, false,
            new RecipeItemInput(oil.Id, 2, UnitOfMeasure.Tablespoon)), default);

        // Meal = 2 servings rice (260) + 0.5 serving oil (59.5) = 319.5 kcal.
        var meal = await meals.CreateAsync(user, Meal("Combo", false,
            new MealPortionInput(riceDish.Id, 2),
            new MealPortionInput(oilDish.Id, 0.5)), default);

        Assert.Equal(319.5, Nutrient(meal.TotalNutrition, "calories"), 3);
    }

    [Fact]
    public async Task Missing_recipe_portion_is_skipped_gracefully()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);

        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165)), default);
        var dish = await recipes.CreateAsync(user, Recipe("Dish", 1, false,
            new RecipeItemInput(chicken.Id, 100, UnitOfMeasure.Gram)), default);

        var meal = await meals.CreateAsync(user, Meal("Plate", false,
            new MealPortionInput(dish.Id, 1)), default);

        // Deleting the referenced recipe leaves a dangling portion; nutrition should still compute
        // from whatever remains (here: nothing), not throw.
        await recipes.DeleteAsync(user, dish.Id, default);

        var reloaded = await meals.GetAsync(user, meal.Id, default);
        Assert.Equal(0, Nutrient(reloaded.TotalNutrition, "calories"), 3);
        Assert.Null(Assert.Single(reloaded.Portions).RecipeName);
    }

    // ---- fuel type ----

    [Theory]
    [InlineData(FuelType.Food)]
    [InlineData(FuelType.Supplement)]
    [InlineData(FuelType.Hydration)]
    public async Task Create_persists_and_round_trips_each_fuel_type(FuelType fuelType)
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        var created = await meals.CreateAsync(user,
            new MealInput("Source", null, FuelType: fuelType), default);
        Assert.Equal(fuelType, created.FuelType);

        // Re-read to confirm it persisted (and that the EF mapping round-trips it).
        var reloaded = await meals.GetAsync(user, created.Id, default);
        Assert.Equal(fuelType, reloaded.FuelType);
    }

    [Fact]
    public async Task Create_defaults_fuel_type_to_food_when_omitted()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        var created = await meals.CreateAsync(user, Meal("No fuel type set", false), default);

        Assert.Equal(FuelType.Food, created.FuelType);
    }

    [Fact]
    public async Task Legacy_meal_with_null_fuel_type_reads_back_as_food()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        // Simulate an old document persisted before the field existed: FuelType left null in storage.
        var legacy = new Meal { Name = "Legacy", OwnerUserId = user.Id, FuelType = null };
        db.Meals.Add(legacy);
        await db.SaveChangesAsync();

        var dto = await meals.GetAsync(user, legacy.Id, default);
        Assert.Equal(FuelType.Food, dto.FuelType);
    }

    [Fact]
    public async Task Update_changes_fuel_type()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        var created = await meals.CreateAsync(user,
            new MealInput("Drink", null, FuelType: FuelType.Food), default);

        var updated = await meals.UpdateAsync(user, created.Id,
            new MealInput("Drink", null, FuelType: FuelType.Hydration), default);
        Assert.Equal(FuelType.Hydration, updated.FuelType);

        var reloaded = await meals.GetAsync(user, created.Id, default);
        Assert.Equal(FuelType.Hydration, reloaded.FuelType);
    }

    [Fact]
    public async Task Validation_rejects_undefined_fuel_type()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, meals) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            meals.CreateAsync(user, new MealInput("Bad", null, FuelType: (FuelType)99), default));
    }

    [Fact]
    public async Task Public_recipe_can_be_portioned_into_another_users_meal()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165)), default);
        // User authors a recipe and gets it approved to public.
        var dish = await recipes.CreateAsync(user, Recipe("Public Dish", 1, false,
            new RecipeItemInput(chicken.Id, 100, UnitOfMeasure.Gram)), default);
        await recipes.SubmitForReviewAsync(user, dish.Id, default);
        await recipes.ApproveAsync(admin, dish.Id, default);

        // Another user can portion the public recipe into their own meal.
        var meal = await meals.CreateAsync(other!, Meal("Other's Plate", false,
            new MealPortionInput(dish.Id, 2)), default);

        Assert.Equal(330, Nutrient(meal.TotalNutrition, "calories"), 3);
    }
}
