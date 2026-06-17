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
        WeightService Weights, IUserRepository Users);

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
        var weights = new WeightService(new WeightEntryRepository(db), TestHelpers.NewAwards(db));
        var profile = new UserProfileService(users, weights, plans);
        var consumption = new ConsumptionService(
            new ConsumptionEntryRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db),
            new WeightEntryRepository(db), plans, TestHelpers.NewAwards(db));

        return new Ctx(admin!, user!, ingredients, recipes, meals, plans, profile, consumption, weights, users);
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
    public async Task Completing_with_servings_logs_that_many_and_scales_the_day_total()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var entry = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 2), default);

        Assert.Equal(2, entry.Servings);
        Assert.Equal(600, Kcal(entry.Nutrition), 1); // 2× the planned single serving

        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Single(day.Entries);
        Assert.Equal(600, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Re_completing_with_different_servings_updates_the_entry_no_duplicate()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var first = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 2), default);
        // Adjust down to half a serving after the fact.
        var updated = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 0.5), default);

        Assert.Equal(first.Id, updated.Id); // same entry, edited in place
        Assert.Equal(0.5, updated.Servings);
        Assert.Equal(150, Kcal(updated.Nutrition), 1);

        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Single(day.Entries); // no duplicate
        Assert.Equal(150, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Re_completing_without_servings_preserves_the_existing_amount()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 3), default);
        // A plain re-complete (no servings) must not clobber the chosen amount back to 1.
        var again = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(3, again.Servings);
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Equal(900, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Completing_without_servings_defaults_to_one()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var entry = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(1, entry.Servings);
        Assert.Equal(300, Kcal(entry.Nutrition), 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InputLimits.ConsumptionServingsMax + 1)]
    public async Task Completing_rejects_invalid_servings(double servings)
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await Assert.ThrowsAsync<ValidationException>(() =>
            c.Consumption.CompletePlannedMealAsync(
                c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: servings), default));
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

    // ---- skipping / un-skipping tasks ----

    [Fact]
    public async Task Skipping_a_task_marks_it_skipped_drops_it_from_pending_and_adds_no_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var lunch = await CaloricMealAsync(c, "lunch", 500);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(breakfast.Id), new(lunch.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var breakfastTask = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks
            .Single(t => t.MealName == "breakfast");
        await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(breakfastTask.Ref, Today), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        var skipped = planned.Tasks.Single(t => t.MealName == "breakfast");
        Assert.True(skipped.Skipped);
        Assert.False(skipped.Done);          // skipped is NOT done
        Assert.Null(skipped.CompletedEntryId);
        // The other task is untouched and still pending.
        Assert.False(planned.Tasks.Single(t => t.MealName == "lunch").Skipped);
        // Only one task remains "still to do" (pending = not done, not skipped).
        Assert.Single(planned.Tasks.Where(t => !t.Done && !t.Skipped));

        // The skip never appears as a logged entry nor adds to the day total.
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Empty(day.Entries);
        Assert.Equal(0, Kcal(day.TotalNutrition), 1);
    }

    [Fact]
    public async Task Skipping_excludes_the_meal_from_the_daily_nutrition_rollup()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(breakfast.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        // The roll-up that feeds goals/dashboard must not include the skipped meal — the day has no entry.
        var rollup = await c.Consumption.DailyNutritionInRangeAsync(c.User, Today, Today, default);
        Assert.False(rollup.ContainsKey(Today));
    }

    [Fact]
    public async Task Skipping_is_idempotent_and_returns_the_same_skip_entry()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var first = await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);
        var second = await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(first.Id, second.Id); // same skip entry, no duplicate
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Empty(day.Entries);
    }

    [Fact]
    public async Task Unskipping_restores_the_task_to_pending()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);
        // Undo reuses the un-complete path (it deletes any entry carrying the ref).
        await c.Consumption.UncompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        var refreshed = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.False(refreshed.Skipped);
        Assert.False(refreshed.Done); // back to pending
    }

    [Fact]
    public async Task Skipping_a_completed_task_replaces_the_completion_and_clears_its_nutrition()
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
        // Changed their mind: skip the now-completed meal.
        await c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        var refreshed = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.True(refreshed.Skipped);
        Assert.False(refreshed.Done);
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Empty(day.Entries);
        Assert.Equal(0, Kcal(day.TotalNutrition), 1); // the prior completion's 300 kcal is gone
    }

    [Fact]
    public async Task Skipping_rejects_a_ref_not_in_the_active_plan()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var meal = await CaloricMealAsync(c, "breakfast", 300);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(meal.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            c.Consumption.SkipPlannedMealAsync(c.User, new CompletePlannedMealInput("bogus:ref::"), default));
    }

    // ---- meal options (a slot offering several meals to choose between) ----

    [Fact]
    public void ScheduledMeal_AlternativeMealIds_reads_empty_not_null_for_legacy_docs()
    {
        // Plan documents written before AlternativeMealIds existed materialize the property as null on
        // read (the Cosmos provider skips the field initializer for an absent JSON property). The getter
        // must coalesce so the summary/task-building code never dereferences null. See the entity comment.
        var s = new ScheduledMeal { MealId = "m1", AlternativeMealIds = null! };
        Assert.NotNull(s.AlternativeMealIds);
        Assert.Empty(s.AlternativeMealIds);
    }

    [Fact]
    public async Task A_slot_with_options_exposes_them_default_first_and_the_summary_uses_the_default()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.NotNull(task.Options);
        Assert.Equal(new[] { eggs.Id, oats.Id }, task.Options!.Select(o => o.MealId)); // default first
        Assert.Equal(300, Kcal(task.Nutrition), 1); // the task's nutrition is the default's

        // The plan's nutrition summary is driven by the default option only.
        var reloaded = await c.Plans.GetAsync(c.User, plan.Id, default);
        Assert.Equal(300, Kcal(reloaded.Summary.DailyAverage), 1);
        var scheduled = reloaded.ScheduledMeals.Single();
        Assert.Equal(eggs.Id, scheduled.MealId);
        Assert.Equal(new[] { oats.Id }, scheduled.Alternatives!.Select(a => a.MealId));
    }

    [Fact]
    public async Task Completing_without_a_choice_logs_the_default_option()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var entry = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        Assert.Equal(eggs.Id, entry.SourceId);
        Assert.Equal(300, Kcal(entry.Nutrition), 1);
    }

    [Fact]
    public async Task Completing_with_a_chosen_alternative_logs_that_option_not_the_default()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var entry = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, ChosenMealId: oats.Id), default);

        Assert.Equal(oats.Id, entry.SourceId);
        Assert.Equal(oats.Name, entry.Name);
        Assert.Equal(450, Kcal(entry.Nutrition), 1);
        // The task still correlates as done via the slot's (default-based) ref.
        var refreshed = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        Assert.True(refreshed.Done);
        Assert.Equal(entry.Id, refreshed.CompletedEntryId);
    }

    [Fact]
    public async Task Swapping_the_chosen_option_repoints_the_existing_entry_no_duplicate()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        var first = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 2), default);
        var swapped = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, ChosenMealId: oats.Id), default);

        Assert.Equal(first.Id, swapped.Id); // same entry, re-pointed
        Assert.Equal(oats.Id, swapped.SourceId);
        Assert.Equal(2, swapped.Servings); // servings preserved across the swap
        Assert.Equal(900, Kcal(swapped.Nutrition), 1); // 2 × 450
        var day = await c.Consumption.GetDayAsync(c.User, Today, default);
        Assert.Single(day.Entries);
    }

    [Fact]
    public async Task Adjusting_servings_after_choosing_an_alternative_does_not_reset_to_the_default()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, ChosenMealId: oats.Id), default);
        // A later servings-only re-complete (no choice) must keep the chosen alternative.
        var updated = await c.Consumption.CompletePlannedMealAsync(
            c.User, new CompletePlannedMealInput(task.Ref, Today, Servings: 3), default);

        Assert.Equal(oats.Id, updated.SourceId);
        Assert.Equal(3, updated.Servings);
    }

    [Fact]
    public async Task Completing_with_a_meal_that_is_not_one_of_the_slots_options_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var notAnOption = await CaloricMealAsync(c, "burger", 800);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { oats.Id }) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).Tasks.Single();
        await Assert.ThrowsAsync<ValidationException>(() =>
            c.Consumption.CompletePlannedMealAsync(
                c.User, new CompletePlannedMealInput(task.Ref, Today, ChosenMealId: notAnOption.Id), default));
    }

    [Fact]
    public async Task Alternatives_are_normalized_dropping_the_default_and_duplicates()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        var oats = await CaloricMealAsync(c, "oats", 450);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                // Default repeated, an alternative duplicated, and a blank — all should be cleaned out.
                new List<ScheduledMealInput>
                {
                    new(eggs.Id, AlternativeMealIds: new[] { eggs.Id, oats.Id, oats.Id, "" }),
                }), default);

        var scheduled = (await c.Plans.GetAsync(c.User, plan.Id, default)).ScheduledMeals.Single();
        Assert.Equal(new[] { oats.Id }, scheduled.Alternatives!.Select(a => a.MealId));
    }

    [Fact]
    public async Task An_alternative_meal_not_available_to_the_user_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var eggs = await CaloricMealAsync(c, "eggs", 300);
        // A meal owned by the admin (User scope) is not usable by the regular user.
        var theirs = await c.Meals.CreateAsync(c.Admin,
            new MealInput("theirs", new List<MealPortionInput>(), false, 1, FuelType.Food), default);

        await Assert.ThrowsAsync<ValidationException>(() =>
            c.Plans.CreateAsync(c.User,
                new MealPlanInput("Daily Plan", MealPlanCadence.Daily,
                    new List<ScheduledMealInput> { new(eggs.Id, AlternativeMealIds: new[] { theirs.Id }) }), default));
    }

    // ---- recurring "Log your weight" task ----

    [Fact]
    public async Task No_weight_schedule_means_no_weight_task()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        Assert.Null(planned.WeightTask);
    }

    [Fact]
    public async Task Weight_task_appears_on_a_due_day_and_is_not_done_until_a_weight_is_recorded()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        // Daily schedule starting today → today is always due, even with no meal plan.
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Daily, Today), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        Assert.NotNull(planned.WeightTask);
        Assert.False(planned.WeightTask!.Done);
        Assert.Null(planned.WeightTask.RecordedValue);
    }

    [Fact]
    public async Task Weight_task_is_done_when_a_weight_exists_for_that_day()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Daily, Today), default);
        await c.Weights.RecordAsync(c.User, new WeightEntryInput(180, WeightUnit.Pound, Today), default);

        var planned = await c.Consumption.GetPlannedDayAsync(c.User, Today, default);
        Assert.NotNull(planned.WeightTask);
        Assert.True(planned.WeightTask!.Done);
        Assert.Equal(180, planned.WeightTask.RecordedValue);
        Assert.Equal(WeightUnit.Pound, planned.WeightTask.RecordedUnit);
    }

    [Fact]
    public async Task Weight_task_does_not_appear_on_a_non_due_day()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        // Weekly schedule anchored on today → today is due, but tomorrow (different DOW) is not.
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Weekly, Today), default);

        Assert.NotNull((await c.Consumption.GetPlannedDayAsync(c.User, Today, default)).WeightTask);
        Assert.Null((await c.Consumption.GetPlannedDayAsync(c.User, Today.AddDays(1), default)).WeightTask);
        // Same day-of-week next week is due again.
        Assert.NotNull((await c.Consumption.GetPlannedDayAsync(c.User, Today.AddDays(7), default)).WeightTask);
    }
}
