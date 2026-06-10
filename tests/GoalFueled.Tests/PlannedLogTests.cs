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

/// <summary>
/// Covers The Log's plan-driven behavior: deriving today's planned-meal tasks from the active plan
/// (weekly vs daily), completing a task (records the right consumption entry, no double-count),
/// un-completing it, day totals, and persisting the active-plan selection.
/// </summary>
public class PlannedLogTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Ctx(
        AppUser Admin, AppUser User,
        IngredientService Ingredients, RecipeService Recipes, MealService Meals,
        MealPlanService Plans, UserProfileService Profile, ConsumptionService Consumption,
        IUserRepository Users);

    private static async Task<Ctx> SetupAsync(GoalFueledDbContext db)
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
        var users = new UserRepository(db);
        var profile = new UserProfileService(users, new WeightService(new WeightEntryRepository(db)), plans);
        var consumption = new ConsumptionService(
            new ConsumptionEntryRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db), plans);

        return new Ctx(admin!, user!, ingredients, recipes, meals, plans, profile, consumption, users);
    }

    private static double Kcal(IReadOnlyList<NutrientAmount> n) =>
        n.FirstOrDefault(a => a.NutrientId == "calories")?.Value ?? 0;

    // Builds a meal worth exactly `calories` kcal (1 serving of a 1-serving recipe of `calories` g
    // of a "1 kcal/g" app ingredient), owned by `user`.
    private static async Task<MealDto> CaloricMealAsync(Ctx c, string name, double calories)
    {
        var ing = await c.Ingredients.CreateAsync(c.Admin,
            new IngredientInput($"{name}-base", null, null, UnitOfMeasure.Gram, 1,
                new List<NutrientAmount> { new("calories", 1) }, true), default);
        var recipe = await c.Recipes.CreateAsync(c.User,
            new RecipeInput($"{name}-recipe", null, null,
                new List<RecipeItemInput> { new(ing.Id, calories, UnitOfMeasure.Gram) }, 1, false), default);
        return await c.Meals.CreateAsync(c.User,
            new MealInput(name, new List<MealPortionInput> { new(recipe.Id, 1) }, false), default);
    }

    // ---- active-plan selection persistence ----

    [Fact]
    public async Task Set_active_plan_persists_and_clears()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);

        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);
        var reloaded = await c.Users.GetByIdAsync(c.User.Id, default);
        Assert.Equal(plan.Id, reloaded!.ActiveMealPlanId);

        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(null), default);
        reloaded = await c.Users.GetByIdAsync(c.User.Id, default);
        Assert.Null(reloaded!.ActiveMealPlanId);
    }

    [Fact]
    public async Task Set_active_plan_rejects_a_plan_not_visible_to_the_user()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput("does-not-exist"), default));
    }

    // ---- deriving today's tasks ----

    [Fact]
    public async Task No_active_plan_yields_empty_planned_day()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        Assert.Null(planned.PlanId);
        Assert.Empty(planned.Tasks);
    }

    [Fact]
    public async Task Daily_plan_lists_all_scheduled_meals_as_tasks()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var lunch = await CaloricMealAsync(c, "lunch", 500);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput>
                {
                    new(breakfast.Id, Time: new TimeOnly(8, 0)),
                    new(lunch.Id, Time: new TimeOnly(12, 0)),
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);

        Assert.Equal(plan.Id, planned.PlanId);
        Assert.Equal("Daily Plan", planned.PlanName);
        Assert.Equal(2, planned.Tasks.Count);
        Assert.All(planned.Tasks, t => Assert.False(t.Done));
        Assert.Contains(planned.Tasks, t => t.MealName == "breakfast" && Kcal(t.Nutrition) == 300);
        Assert.Contains(planned.Tasks, t => t.MealName == "lunch" && Kcal(t.Nutrition) == 500);
    }

    [Fact]
    public async Task Weekly_plan_lists_only_todays_pinned_meals_plus_dayless_meals()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var todayMeal = await CaloricMealAsync(c, "today-meal", 300);
        var otherDayMeal = await CaloricMealAsync(c, "other-day-meal", 400);
        var everyDayMeal = await CaloricMealAsync(c, "every-day-meal", 100);

        var otherDay = Today.DayOfWeek == DayOfWeek.Monday ? DayOfWeek.Tuesday : DayOfWeek.Monday;
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Weekly Plan", MealPlanCadence.Weekly,
                new List<ScheduledMealInput>
                {
                    new(todayMeal.Id, DayOfWeek: Today.DayOfWeek, Time: new TimeOnly(8, 0)),
                    new(otherDayMeal.Id, DayOfWeek: otherDay, Time: new TimeOnly(8, 0)),
                    new(everyDayMeal.Id), // day-less → applies every day
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);

        Assert.Equal(2, planned.Tasks.Count); // today's pinned + the day-less one
        Assert.Contains(planned.Tasks, t => t.MealName == "today-meal");
        Assert.Contains(planned.Tasks, t => t.MealName == "every-day-meal");
        Assert.DoesNotContain(planned.Tasks, t => t.MealName == "other-day-meal");
    }

    [Fact]
    public async Task Untimed_meal_items_appear_as_anytime_tasks_but_ingredient_items_do_not()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var snack = await CaloricMealAsync(c, "snack", 150);
        var water = await c.Ingredients.CreateAsync(c.Admin,
            new IngredientInput("water", null, null, UnitOfMeasure.Milliliter, 250,
                new List<NutrientAmount>(), true), default);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                ScheduledMeals: null,
                UntimedItems: new List<UntimedItemInput>
                {
                    new(MealId: snack.Id),
                    new(IngredientId: water.Id, Quantity: 500),
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);

        Assert.Single(planned.Tasks);
        Assert.Equal("snack", planned.Tasks[0].MealName);
        Assert.Null(planned.Tasks[0].Time); // anytime
    }

    // ---- completing / un-completing tasks ----

    [Fact]
    public async Task Completing_a_task_records_a_meal_consumption_entry_and_marks_it_done()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id, Time: new TimeOnly(8, 0)) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var entry = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(ConsumptionSource.Meal, entry.Source);
        Assert.Equal(meal.Id, entry.SourceId);
        Assert.Equal(1, entry.Servings);
        Assert.Equal(300, Kcal(entry.Nutrition), 1);
        Assert.Equal(task.Ref, entry.PlannedMealRef);

        // The task now shows done and points at the completion entry.
        var refreshed = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.True(refreshed.Done);
        Assert.Equal(entry.Id, refreshed.CompletedEntryId);

        // The day total includes the completed meal exactly once.
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Single(day.Entries);
        Assert.Equal(300, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Completing_the_same_task_twice_is_idempotent_no_double_count()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var first = await c.Consumption.CompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);
        var second = await c.Consumption.CompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(first.Id, second.Id); // same entry returned, not a duplicate
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Single(day.Entries);
        Assert.Equal(300, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Uncompleting_a_task_removes_the_entry_and_resets_the_day_total()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.CompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);
        await c.Consumption.UncompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        var refreshed = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.False(refreshed.Done);
        Assert.Null(refreshed.CompletedEntryId);

        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Empty(day.Entries);
        Assert.Equal(0, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Completing_a_planned_meal_and_an_adhoc_log_of_the_same_meal_count_separately()
    {
        // A completed planned meal and a separate ad-hoc log of the same meal are two real intakes — both
        // count. The plan task is matched only to its tagged completion entry, never to the ad-hoc one.
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.CompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);
        // Ad-hoc log of the same meal (e.g. ate it twice).
        await c.Consumption.LogAsync(c.User,
            new ConsumptionEntryInput(ConsumptionSource.Meal, meal.Id, Servings: 1), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        Assert.True(planned.Tasks.Single().Done); // task still matched to its own completion only

        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Equal(2, day.Entries.Count);
        Assert.Equal(600, Kcal(day.TotalNutrition), 1); // both counted
    }

    [Fact]
    public async Task Completing_rejects_a_ref_not_in_the_active_plan()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            c.Consumption.CompletePlannedMealAsync(
                c.User, new CompletePlannedMealInput("bogus:ref::"), default));
    }

    [Fact]
    public async Task Completing_without_an_active_plan_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            c.Consumption.CompletePlannedMealAsync(
                c.User, new CompletePlannedMealInput("any:ref::"), default));
    }
}
