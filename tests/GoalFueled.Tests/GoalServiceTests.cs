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

public class GoalServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(AppUser admin, AppUser user, AppUser other,
        WeightService weights, IngredientService ingredients, RecipeService recipes,
        MealService meals, MealPlanService plans, GoalService goals)> SetupAsync(GoalFueledDbContext db)
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

        var weights = new WeightService(new WeightEntryRepository(db));
        var ingredients = new IngredientService(new IngredientRepository(db));
        var recipes = new RecipeService(new RecipeRepository(db), new IngredientRepository(db));
        var meals = new MealService(new MealRepository(db), new RecipeRepository(db), new IngredientRepository(db));
        var plans = new MealPlanService(
            new MealPlanRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));
        var goals = new GoalService(
            new GoalRepository(db), new WeightEntryRepository(db), new MealPlanRepository(db),
            new MealRepository(db), new RecipeRepository(db), new IngredientRepository(db));

        return (admin!, user!, other!, weights, ingredients, recipes, meals, plans, goals);
    }

    private static GoalInput WeightGoal(
        double target, GoalCadence cadence = GoalCadence.OneTime, bool exceedingPositive = true,
        DateOnly? start = null, DateOnly? targetDate = null, int? recurrenceDays = null) =>
        new("My weight goal", GoalMetricType.Weight, target, cadence, exceedingPositive,
            NutrientId: null, StartDate: start, TargetDate: targetDate, RecurrenceDays: recurrenceDays);

    private static GoalInput NutrientGoal(
        string nutrientId, double target, GoalCadence cadence, bool exceedingPositive,
        DateOnly? start = null, DateOnly? targetDate = null, int? recurrenceDays = null) =>
        new("My nutrient goal", GoalMetricType.Nutrient, target, cadence, exceedingPositive,
            NutrientId: nutrientId, StartDate: start, TargetDate: targetDate, RecurrenceDays: recurrenceDays);

    // Builds + assigns to a daily plan a meal supplying exactly `calories` kcal/day for `user`.
    private static async Task BuildCaloricDailyPlanAsync(
        AppUser admin, AppUser user, IngredientService ingredients, RecipeService recipes,
        MealService meals, MealPlanService plans, double calories)
    {
        var ing = await ingredients.CreateAsync(admin,
            new IngredientInput("kcal-base", null, null, UnitOfMeasure.Gram, 1,
                new List<NutrientAmount> { new("calories", 1) }, true), default);
        var recipe = await recipes.CreateAsync(user,
            new RecipeInput("kcal-recipe", null, null,
                new List<RecipeItemInput> { new(ing.Id, calories, UnitOfMeasure.Gram) }, 1, false), default);
        var meal = await meals.CreateAsync(user,
            new MealInput("kcal-meal", new List<MealPortionInput> { new(recipe.Id, 1) }, false), default);
        await plans.CreateAsync(user,
            new MealPlanInput("Daily plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }, null), default);
    }

    // ---- CRUD & ownership ----

    [Fact]
    public async Task Create_persists_goal_owned_by_user()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);

        var created = await goals.CreateAsync(user,
            WeightGoal(170, targetDate: Today.AddDays(30)), default);

        Assert.Equal(GoalMetricType.Weight, created.MetricType);
        Assert.Equal(170, created.TargetValue);
        Assert.Equal(GoalCadence.OneTime, created.Cadence);

        var list = await goals.ListAsync(user, default);
        Assert.Single(list);
    }

    [Fact]
    public async Task Users_cannot_see_or_get_each_others_goals()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, other, _, _, _, _, _, goals) = await SetupAsync(db);

        var mine = await goals.CreateAsync(user, WeightGoal(170, targetDate: Today.AddDays(30)), default);
        await goals.CreateAsync(other, WeightGoal(200, targetDate: Today.AddDays(30)), default);

        Assert.Single(await goals.ListAsync(user, default));
        Assert.Single(await goals.ListAsync(other, default));
        // Other cannot read mine (404, surfaced as NotFound).
        await Assert.ThrowsAsync<NotFoundException>(() => goals.GetAsync(other, mine.Id, default));
    }

    [Fact]
    public async Task Update_and_delete_only_by_owner()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, other, _, _, _, _, _, goals) = await SetupAsync(db);
        var mine = await goals.CreateAsync(user, WeightGoal(170, targetDate: Today.AddDays(30)), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            goals.UpdateAsync(other, mine.Id, WeightGoal(150, targetDate: Today.AddDays(30)), default));
        await Assert.ThrowsAsync<NotFoundException>(() => goals.DeleteAsync(other, mine.Id, default));

        var updated = await goals.UpdateAsync(user, mine.Id, WeightGoal(160, targetDate: Today.AddDays(30)), default);
        Assert.Equal(160, updated.TargetValue);

        await goals.DeleteAsync(user, mine.Id, default);
        Assert.Empty(await goals.ListAsync(user, default));
    }

    // ---- validation ----

    [Fact]
    public async Task OneTime_goal_requires_target_date()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            goals.CreateAsync(user, WeightGoal(170, GoalCadence.OneTime, targetDate: null), default));
    }

    [Fact]
    public async Task Recurring_goal_requires_recurrence_days()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            goals.CreateAsync(user, WeightGoal(170, GoalCadence.Recurring, recurrenceDays: null), default));
    }

    [Fact]
    public async Task Nutrient_goal_requires_known_nutrient()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            goals.CreateAsync(user, NutrientGoal("not_a_nutrient", 2000, GoalCadence.Recurring, false, recurrenceDays: 7), default));
    }

    [Fact]
    public async Task Validation_rejects_non_positive_target_and_bad_dates()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            goals.CreateAsync(user, WeightGoal(0, targetDate: Today.AddDays(10)), default));
        // Target date before start date.
        await Assert.ThrowsAsync<ValidationException>(() =>
            goals.CreateAsync(user, WeightGoal(170, start: Today, targetDate: Today.AddDays(-1)), default));
    }

    // ---- weight progress ----

    [Fact]
    public async Task Weight_goal_with_no_entries_reports_no_data()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);

        var goal = await goals.CreateAsync(user, WeightGoal(170, targetDate: Today.AddDays(30)), default);
        Assert.Equal(GoalStatus.NoData, goal.Progress.Status);
    }

    [Fact]
    public async Task Weight_loss_goal_tracks_progress_from_baseline()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, weights, _, _, _, _, goals) = await SetupAsync(db);

        // Baseline 200, currently 190, target 170 → 30% of the way (200→170 = 30 lbs span, lost 10).
        await weights.RecordAsync(user, new WeightEntryInput(200, WeightUnit.Pound, Today.AddDays(-10)), default);
        await weights.RecordAsync(user, new WeightEntryInput(190, WeightUnit.Pound, Today), default);

        var goal = await goals.CreateAsync(user,
            WeightGoal(170, exceedingPositive: true, start: Today.AddDays(-10), targetDate: Today.AddDays(30)), default);

        Assert.Equal(GoalStatus.InProgress, goal.Progress.Status);
        Assert.Equal(200, goal.Progress.BaselineValue);
        Assert.Equal(190, goal.Progress.CurrentValue);
        Assert.Equal(33.3, goal.Progress.Percent, 1);
        Assert.True(goal.Progress.OutcomePositive);
    }

    [Fact]
    public async Task Weight_loss_goal_achieved_and_exceeded_celebrates_when_exceeding_positive()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, weights, _, _, _, _, goals) = await SetupAsync(db);

        await weights.RecordAsync(user, new WeightEntryInput(200, WeightUnit.Pound, Today.AddDays(-10)), default);
        await weights.RecordAsync(user, new WeightEntryInput(165, WeightUnit.Pound, Today), default); // below 170

        var goal = await goals.CreateAsync(user,
            WeightGoal(170, exceedingPositive: true, start: Today.AddDays(-10), targetDate: Today.AddDays(30)), default);

        Assert.Equal(GoalStatus.Achieved, goal.Progress.Status);
        Assert.Equal(100, goal.Progress.Percent);
        Assert.True(goal.Progress.ExceededTarget);
        Assert.True(goal.Progress.OutcomePositive); // exceeding is good here
    }

    [Fact]
    public async Task Weight_goal_missed_after_deadline_is_negative_outcome()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, weights, _, _, _, _, goals) = await SetupAsync(db);

        await weights.RecordAsync(user, new WeightEntryInput(200, WeightUnit.Pound, Today.AddDays(-30)), default);
        await weights.RecordAsync(user, new WeightEntryInput(195, WeightUnit.Pound, Today), default);

        // Deadline already in the past, target not met.
        var goal = await goals.CreateAsync(user,
            WeightGoal(170, start: Today.AddDays(-30), targetDate: Today.AddDays(-1)), default);

        Assert.Equal(GoalStatus.Missed, goal.Progress.Status);
        Assert.False(goal.Progress.OutcomePositive);
    }

    // ---- nutrient progress ----

    [Fact]
    public async Task Nutrient_goal_with_no_plan_reports_no_data()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);

        var goal = await goals.CreateAsync(user,
            NutrientGoal("calories", 2000, GoalCadence.Recurring, exceedingPositive: false, recurrenceDays: 7), default);

        Assert.Equal(GoalStatus.NoData, goal.Progress.Status);
    }

    [Fact]
    public async Task Recurring_calorie_goal_projects_planned_intake_over_period()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, ingredients, recipes, meals, plans, goals) = await SetupAsync(db);

        // Plan supplies 2000 kcal/day; goal is 14000 kcal every 7 days → exactly on target.
        await BuildCaloricDailyPlanAsync(admin, user, ingredients, recipes, meals, plans, 2000);

        var goal = await goals.CreateAsync(user,
            NutrientGoal("calories", 14000, GoalCadence.Recurring, exceedingPositive: false, recurrenceDays: 7), default);

        Assert.Equal(GoalStatus.Achieved, goal.Progress.Status);
        Assert.Equal(14000, goal.Progress.CurrentValue, 1);
        Assert.False(goal.Progress.ExceededTarget);
        Assert.True(goal.Progress.OutcomePositive); // on target, not over
    }

    [Fact]
    public async Task Calorie_budget_overshoot_is_negative_when_exceeding_is_bad()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, ingredients, recipes, meals, plans, goals) = await SetupAsync(db);

        // Plan supplies 2500 kcal/day; weekly budget 14000 → 17500 projected, over budget.
        await BuildCaloricDailyPlanAsync(admin, user, ingredients, recipes, meals, plans, 2500);

        var goal = await goals.CreateAsync(user,
            NutrientGoal("calories", 14000, GoalCadence.Recurring, exceedingPositive: false, recurrenceDays: 7), default);

        Assert.True(goal.Progress.ExceededTarget);
        Assert.False(goal.Progress.OutcomePositive); // going over a budget is bad
    }

    [Fact]
    public async Task Protein_target_in_progress_when_below_target()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, _, ingredients, recipes, meals, plans, goals) = await SetupAsync(db);

        // Build a plan supplying 50 g protein/day.
        var ing = await ingredients.CreateAsync(admin,
            new IngredientInput("protein-base", null, null, UnitOfMeasure.Gram, 1,
                new List<NutrientAmount> { new("protein", 1) }, true), default);
        var recipe = await recipes.CreateAsync(user,
            new RecipeInput("protein-recipe", null, null,
                new List<RecipeItemInput> { new(ing.Id, 50, UnitOfMeasure.Gram) }, 1, false), default);
        var meal = await meals.CreateAsync(user,
            new MealInput("protein-meal", new List<MealPortionInput> { new(recipe.Id, 1) }, false), default);
        await plans.CreateAsync(user,
            new MealPlanInput("Daily plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }, null), default);

        // Daily goal of 100 g protein, exceeding is positive (more protein is good). 50 < 100 → 50%.
        var goal = await goals.CreateAsync(user,
            NutrientGoal("protein", 100, GoalCadence.Recurring, exceedingPositive: true, recurrenceDays: 1), default);

        Assert.Equal(GoalStatus.InProgress, goal.Progress.Status);
        Assert.Equal(50, goal.Progress.CurrentValue, 1);
        Assert.Equal(50, goal.Progress.Percent, 1);
    }

    [Fact]
    public async Task DTO_includes_resolved_nutrient_name_and_unit()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, _, _, _, _, _, goals) = await SetupAsync(db);

        var goal = await goals.CreateAsync(user,
            NutrientGoal("vitamin_c", 90, GoalCadence.Recurring, exceedingPositive: true, recurrenceDays: 1), default);

        Assert.Equal("Vitamin C", goal.NutrientName);
        Assert.Equal("mg", goal.MetricUnit);
    }
}
