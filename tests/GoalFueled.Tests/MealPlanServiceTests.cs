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

public class MealPlanServiceTests
{
    // Provisions an admin (first) and a regular user, plus the full service graph against one context.
    private static async Task<(AppUser admin, AppUser user, IngredientService ingredients,
        RecipeService recipes, MealService meals, MealPlanService plans)> SetupAsync(GoalFueledDbContext db)
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
        var plans = new MealPlanService(
            new MealPlanRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));
        return (admin!, user!, ingredients, recipes, meals, plans);
    }

    private static IngredientInput Ingredient(
        string name, UnitOfMeasure unit, double serving, bool appLevel, params (string id, double v)[] nutrients) =>
        new(name, null, null, unit, serving, nutrients.Select(n => new NutrientAmount(n.id, n.v)).ToList(), appLevel);

    private static RecipeInput Recipe(string name, double servings, bool appLevel, params RecipeItemInput[] items) =>
        new(name, null, null, items.ToList(), servings, appLevel);

    private static MealInput Meal(string name, bool appLevel, params MealPortionInput[] portions) =>
        new(name, portions.ToList(), appLevel);

    private static MealPlanInput Plan(
        string name, MealPlanCadence cadence,
        IReadOnlyList<ScheduledMealInput>? scheduled = null,
        IReadOnlyList<UntimedItemInput>? untimed = null) =>
        new(name, cadence, scheduled, untimed);

    private static double Nutrient(IReadOnlyList<NutrientAmount> amounts, string id) =>
        amounts.SingleOrDefault(a => a.NutrientId == id)?.Value ?? 0;

    // Builds a meal whose total nutrition is exactly `calories` kcal (1 serving of a 1-serving recipe
    // made from `calories` g of a "1 kcal per gram" ingredient).
    private static async Task<MealDto> CaloricMeal(
        AppUser admin, AppUser user, IngredientService ingredients, RecipeService recipes,
        MealService meals, string name, double calories)
    {
        var ing = await ingredients.CreateAsync(admin,
            Ingredient($"{name}-base", UnitOfMeasure.Gram, 1, true, ("calories", 1)), default);
        var recipe = await recipes.CreateAsync(user, Recipe($"{name}-recipe", 1, false,
            new RecipeItemInput(ing.Id, calories, UnitOfMeasure.Gram)), default);
        return await meals.CreateAsync(user, Meal(name, false, new MealPortionInput(recipe.Id, 1)), default);
    }

    // ---- RBAC ----

    [Fact]
    public async Task User_creates_plan_owned_by_them()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, plans) = await SetupAsync(db);

        var created = await plans.CreateAsync(user, Plan("My Day", MealPlanCadence.Daily), default);

        Assert.Equal(user.Id, created.OwnerUserId);
        Assert.True(created.CanEdit);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_plans()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, plans) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await plans.CreateAsync(user, Plan("Secret Plan", MealPlanCadence.Daily), default);

        var otherList = await plans.ListAsync(other!, null, default);
        Assert.DoesNotContain(otherList, p => p.Id == mine.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => plans.GetAsync(other!, mine.Id, default));
    }

    [Fact]
    public async Task Owner_can_update_but_other_user_cannot()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, plans) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await plans.CreateAsync(user, Plan("Mine", MealPlanCadence.Daily), default);

        var updated = await plans.UpdateAsync(user, mine.Id, Plan("Mine v2", MealPlanCadence.Daily), default);
        Assert.Equal("Mine v2", updated.Name);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            plans.UpdateAsync(other!, mine.Id, Plan("Hijacked", MealPlanCadence.Daily), default));
    }

    [Fact]
    public async Task Admin_can_edit_and_delete_another_users_plan()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, _, plans) = await SetupAsync(db);

        var mine = await plans.CreateAsync(user, Plan("User Plan", MealPlanCadence.Daily), default);

        var updated = await plans.UpdateAsync(admin, mine.Id, Plan("Edited by admin", MealPlanCadence.Daily), default);
        Assert.Equal("Edited by admin", updated.Name);

        await plans.DeleteAsync(admin, mine.Id, default);
        await Assert.ThrowsAsync<NotFoundException>(() => plans.GetAsync(admin, mine.Id, default));
    }

    // ---- validation ----

    [Fact]
    public async Task Validation_rejects_empty_name_and_unknown_meal()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, plans) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            plans.CreateAsync(user, Plan("", MealPlanCadence.Daily), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            plans.CreateAsync(user, Plan("Ghost", MealPlanCadence.Daily,
                scheduled: new[] { new ScheduledMealInput("does-not-exist") }), default));
    }

    [Fact]
    public async Task Untimed_item_must_reference_exactly_one_of_meal_or_ingredient()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, _, _, plans) = await SetupAsync(db);
        var water = await ingredients.CreateAsync(admin,
            Ingredient("Water", UnitOfMeasure.Milliliter, 250, true), default);

        // Neither set.
        await Assert.ThrowsAsync<ValidationException>(() =>
            plans.CreateAsync(user, Plan("Bad", MealPlanCadence.Daily,
                untimed: new[] { new UntimedItemInput() }), default));
        // Both set.
        await Assert.ThrowsAsync<ValidationException>(() =>
            plans.CreateAsync(user, Plan("Bad", MealPlanCadence.Daily,
                untimed: new[] { new UntimedItemInput(MealId: "m", IngredientId: water.Id) }), default));
    }

    [Fact]
    public async Task Cannot_reference_another_users_private_meal()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var theirs = await CaloricMeal(admin, other!, ingredients, recipes, meals, "Theirs", 100);

        await Assert.ThrowsAsync<ValidationException>(() =>
            plans.CreateAsync(user, Plan("Steal", MealPlanCadence.Daily,
                scheduled: new[] { new ScheduledMealInput(theirs.Id) }), default));
    }

    // ---- schedule handling ----

    [Fact]
    public async Task Daily_plan_clears_day_of_week_on_scheduled_meals()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var breakfast = await CaloricMeal(admin, user, ingredients, recipes, meals, "Breakfast", 300);

        var plan = await plans.CreateAsync(user, Plan("Daily", MealPlanCadence.Daily,
            scheduled: new[]
            {
                new ScheduledMealInput(breakfast.Id, DayOfWeek.Monday, new TimeOnly(8, 0)),
            }), default);

        var s = Assert.Single(plan.ScheduledMeals);
        Assert.Null(s.DayOfWeek); // normalized away for daily plans
        Assert.Equal(new TimeOnly(8, 0), s.Time);
        Assert.Equal("Breakfast", s.MealName);
    }

    [Fact]
    public async Task Weekly_plan_preserves_day_and_time()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var dinner = await CaloricMeal(admin, user, ingredients, recipes, meals, "Dinner", 600);

        var plan = await plans.CreateAsync(user, Plan("Weekly", MealPlanCadence.Weekly,
            scheduled: new[]
            {
                new ScheduledMealInput(dinner.Id, DayOfWeek.Friday, new TimeOnly(19, 30)),
            }), default);

        var s = Assert.Single(plan.ScheduledMeals);
        Assert.Equal(DayOfWeek.Friday, s.DayOfWeek);
        Assert.Equal(new TimeOnly(19, 30), s.Time);
    }

    // ---- daily-summary aggregation ----

    [Fact]
    public async Task Daily_summary_sums_scheduled_meals_and_untimed_items()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var breakfast = await CaloricMeal(admin, user, ingredients, recipes, meals, "Breakfast", 300);
        var lunch = await CaloricMeal(admin, user, ingredients, recipes, meals, "Lunch", 500);

        // 1 kcal per gram banana, serving size 100 g (100 kcal/serving). Untimed 50 g → 50 kcal.
        var banana = await ingredients.CreateAsync(admin,
            Ingredient("Banana", UnitOfMeasure.Gram, 100, true, ("calories", 100)), default);

        var plan = await plans.CreateAsync(user, Plan("Daily", MealPlanCadence.Daily,
            scheduled: new[]
            {
                new ScheduledMealInput(breakfast.Id),
                new ScheduledMealInput(lunch.Id),
            },
            untimed: new[]
            {
                new UntimedItemInput(IngredientId: banana.Id, Quantity: 50),
            }), default);

        // One day summary, DayOfWeek null. 300 + 500 + 50 = 850 kcal.
        var day = Assert.Single(plan.Summary.Days);
        Assert.Null(day.DayOfWeek);
        Assert.Equal(850, Nutrient(day.Nutrition, "calories"), 3);
        // Daily plan: weekly total and average mirror the single day.
        Assert.Equal(850, Nutrient(plan.Summary.WeeklyTotal, "calories"), 3);
        Assert.Equal(850, Nutrient(plan.Summary.DailyAverage, "calories"), 3);
    }

    [Fact]
    public async Task Untimed_meal_item_counts_toward_the_summary()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var snack = await CaloricMeal(admin, user, ingredients, recipes, meals, "Snack", 120);

        var plan = await plans.CreateAsync(user, Plan("Daily", MealPlanCadence.Daily,
            untimed: new[] { new UntimedItemInput(MealId: snack.Id) }), default);

        var day = Assert.Single(plan.Summary.Days);
        Assert.Equal(120, Nutrient(day.Nutrition, "calories"), 3);
    }

    [Fact]
    public async Task Weekly_summary_buckets_by_day_and_includes_everyday_items()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var monMeal = await CaloricMeal(admin, user, ingredients, recipes, meals, "Mon", 400);
        var wedMeal = await CaloricMeal(admin, user, ingredients, recipes, meals, "Wed", 600);

        // Untimed water-like item consumed every day: 1 kcal per gram, serving 100 g, qty 100 → 100 kcal.
        var everyday = await ingredients.CreateAsync(admin,
            Ingredient("Everyday", UnitOfMeasure.Gram, 100, true, ("calories", 100)), default);

        var plan = await plans.CreateAsync(user, Plan("Weekly", MealPlanCadence.Weekly,
            scheduled: new[]
            {
                new ScheduledMealInput(monMeal.Id, DayOfWeek.Monday),
                new ScheduledMealInput(wedMeal.Id, DayOfWeek.Wednesday),
            },
            untimed: new[] { new UntimedItemInput(IngredientId: everyday.Id, Quantity: 100) }), default);

        // Only Mon + Wed have consumption (every day also gets the untimed 100, but a day with ONLY the
        // everyday item still counts — so all 7 days appear). Verify Mon and Wed totals specifically.
        var mon = plan.Summary.Days.Single(d => d.DayOfWeek == DayOfWeek.Monday);
        var wed = plan.Summary.Days.Single(d => d.DayOfWeek == DayOfWeek.Wednesday);
        var tue = plan.Summary.Days.Single(d => d.DayOfWeek == DayOfWeek.Tuesday);

        Assert.Equal(500, Nutrient(mon.Nutrition, "calories"), 3);  // 400 + 100 everyday
        Assert.Equal(700, Nutrient(wed.Nutrition, "calories"), 3);  // 600 + 100 everyday
        Assert.Equal(100, Nutrient(tue.Nutrition, "calories"), 3);  // everyday only

        // Weekly total = sum across 7 days = 400 + 600 + 7×100 = 1700. Average = 1700/7.
        Assert.Equal(1700, Nutrient(plan.Summary.WeeklyTotal, "calories"), 3);
        Assert.Equal(1700.0 / 7, Nutrient(plan.Summary.DailyAverage, "calories"), 3);
    }

    [Fact]
    public async Task Deleted_meal_reference_is_skipped_gracefully()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes, meals, plans) = await SetupAsync(db);
        var meal = await CaloricMeal(admin, user, ingredients, recipes, meals, "Temp", 200);

        var plan = await plans.CreateAsync(user, Plan("Daily", MealPlanCadence.Daily,
            scheduled: new[] { new ScheduledMealInput(meal.Id) }), default);

        await meals.DeleteAsync(user, meal.Id, default);

        var reloaded = await plans.GetAsync(user, plan.Id, default);
        var day = Assert.Single(reloaded.Summary.Days);
        Assert.Equal(0, Nutrient(day.Nutrition, "calories"), 3);
        Assert.Null(Assert.Single(reloaded.ScheduledMeals).MealName);
    }
}
