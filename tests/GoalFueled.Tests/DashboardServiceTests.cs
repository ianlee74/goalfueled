using GoalFueled.Api.Auth;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

/// <summary>
/// Covers the dashboard aggregation: next-3-upcoming-meal selection/ordering (incl. skipping completed
/// and rolling into later plan days), the weight-due flag, and the 7-day fuel/burn/kindle/net series with
/// its estimated weight change.
/// </summary>
public class DashboardServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    // A fixed "now" early in the day so all of today's timed meals count as still-upcoming.
    private static readonly DateTimeOffset NowEarly =
        new(Today.Year, Today.Month, Today.Day, 6, 0, 0, TimeSpan.Zero);

    private sealed record Ctx(
        AppUser Admin, AppUser User,
        IngredientService Ingredients, RecipeService Recipes, MealService Meals,
        MealPlanService Plans, UserProfileService Profile, ConsumptionService Consumption,
        BurnService Burn, WeightService Weights, DashboardService Dashboard, IUserRepository Users);

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
        var burn = new BurnService(new BurnActivityRepository(db), new WeightEntryRepository(db), TestHelpers.NewAwards(db));
        var dashboard = new DashboardService(plans, consumption, burn, new WeightEntryRepository(db));

        return new Ctx(admin!, user!, ingredients, recipes, meals, plans, profile,
            consumption, burn, weights, dashboard, users);
    }

    // Builds a meal worth exactly `calories` kcal, owned by the user.
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

    // ---- upcoming meals: selection & ordering ----

    [Fact]
    public async Task No_active_plan_yields_no_upcoming_meals()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);
        Assert.Null(dash.ActivePlanId);
        Assert.Empty(dash.UpcomingMeals);
    }

    [Fact]
    public async Task Upcoming_lists_at_most_three_todays_meals_ordered_by_time()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var lunch = await CaloricMealAsync(c, "lunch", 500);
        var dinner = await CaloricMealAsync(c, "dinner", 700);
        var supper = await CaloricMealAsync(c, "supper", 200);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily", MealPlanCadence.Daily,
                new List<ScheduledMealInput>
                {
                    new(dinner.Id, Time: new TimeOnly(18, 0)),
                    new(breakfast.Id, Time: new TimeOnly(8, 0)),
                    new(supper.Id, Time: new TimeOnly(21, 0)),
                    new(lunch.Id, Time: new TimeOnly(12, 0)),
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.Equal(3, dash.UpcomingMeals.Count);
        Assert.Equal(new[] { "breakfast", "lunch", "dinner" },
            dash.UpcomingMeals.Select(m => m.MealName).ToArray());
        Assert.All(dash.UpcomingMeals, m => Assert.False(m.IsLaterDay));
        Assert.Equal(300, dash.UpcomingMeals[0].Calories, 1);
    }

    [Fact]
    public async Task Upcoming_skips_meals_earlier_than_now_and_completed_meals()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var lunch = await CaloricMealAsync(c, "lunch", 500);
        var dinner = await CaloricMealAsync(c, "dinner", 700);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily", MealPlanCadence.Daily,
                new List<ScheduledMealInput>
                {
                    new(breakfast.Id, Time: new TimeOnly(8, 0)),
                    new(lunch.Id, Time: new TimeOnly(12, 0)),
                    new(dinner.Id, Time: new TimeOnly(18, 0)),
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        // Now = 13:00 → breakfast (8:00) and lunch (12:00) are in the past; only dinner remains by time.
        var now = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 13, 0, 0, TimeSpan.Zero);
        var dash = await c.Dashboard.GetAsync(c.User, Today, now, default);

        // Only dinner is left today; the rest roll into tomorrow (a later plan day).
        Assert.Equal("dinner", dash.UpcomingMeals[0].MealName);
        Assert.False(dash.UpcomingMeals[0].IsLaterDay);
        Assert.Equal(3, dash.UpcomingMeals.Count); // dinner + tomorrow's breakfast & lunch
        Assert.True(dash.UpcomingMeals[1].IsLaterDay);
        Assert.Equal(Today.AddDays(1), dash.UpcomingMeals[1].Date);
        Assert.Equal("breakfast", dash.UpcomingMeals[1].MealName);
    }

    [Fact]
    public async Task Upcoming_skips_a_completed_meal_even_if_still_in_the_future()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var breakfast = await CaloricMealAsync(c, "breakfast", 300);
        var lunch = await CaloricMealAsync(c, "lunch", 500);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily", MealPlanCadence.Daily,
                new List<ScheduledMealInput>
                {
                    new(breakfast.Id, Time: new TimeOnly(8, 0)),
                    new(lunch.Id, Time: new TimeOnly(12, 0)),
                }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        // Complete breakfast; with an early "now" both would otherwise be upcoming.
        var task = (await c.Consumption.GetPlannedDayAsync(c.User, Today, default))
            .Tasks.Single(t => t.MealName == "breakfast");
        await c.Consumption.CompletePlannedMealAsync(c.User, new CompletePlannedMealInput(task.Ref, Today), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.DoesNotContain(dash.UpcomingMeals.Where(m => !m.IsLaterDay), m => m.MealName == "breakfast");
        Assert.Contains(dash.UpcomingMeals, m => m.MealName == "lunch" && !m.IsLaterDay);
    }

    [Fact]
    public async Task Untimed_anytime_meals_sort_after_timed_meals()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var timed = await CaloricMealAsync(c, "timed", 300);
        var anytime = await CaloricMealAsync(c, "anytime", 100);
        var plan = await c.Plans.CreateAsync(c.User,
            new MealPlanInput("Daily", MealPlanCadence.Daily,
                new List<ScheduledMealInput> { new(timed.Id, Time: new TimeOnly(8, 0)) },
                new List<UntimedItemInput> { new(MealId: anytime.Id) }), default);
        await c.Profile.SetActiveMealPlanAsync(c.User, new ActiveMealPlanInput(plan.Id), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        // Today contributes two meals; the timed one sorts before the untimed "anytime" one. A third
        // upcoming slot rolls into tomorrow (a later plan day).
        Assert.Equal("timed", dash.UpcomingMeals[0].MealName);
        Assert.False(dash.UpcomingMeals[0].IsLaterDay);
        Assert.Equal("anytime", dash.UpcomingMeals[1].MealName);
        Assert.Null(dash.UpcomingMeals[1].Time);
        Assert.False(dash.UpcomingMeals[1].IsLaterDay);
        Assert.True(dash.UpcomingMeals[2].IsLaterDay); // rolled into the next plan day
    }

    // ---- weight-due flag ----

    [Fact]
    public async Task Weight_due_is_null_when_no_schedule()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);
        Assert.Null(dash.WeightDue);
    }

    [Fact]
    public async Task Weight_due_is_present_and_not_recorded_on_a_due_day()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Daily, Today), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.NotNull(dash.WeightDue);
        Assert.False(dash.WeightDue!.AlreadyRecorded);
        Assert.Null(dash.WeightDue.RecordedValue);
    }

    [Fact]
    public async Task Weight_due_reflects_a_recorded_value()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Daily, Today), default);
        await c.Weights.RecordAsync(c.User, new WeightEntryInput(180, WeightUnit.Pound, Today), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.NotNull(dash.WeightDue);
        Assert.True(dash.WeightDue!.AlreadyRecorded);
        Assert.Equal(180, dash.WeightDue.RecordedValue);
        Assert.Equal(WeightUnit.Pound, dash.WeightDue.RecordedUnit);
    }

    [Fact]
    public async Task Weight_due_is_null_on_a_non_due_day()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        // Weekly anchored today: tomorrow (different weekday) is not due.
        await c.Profile.SetWeightScheduleAsync(c.User,
            new WeightScheduleInput(WeightScheduleCadence.Weekly, Today), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today.AddDays(1), NowEarly, default);
        Assert.Null(dash.WeightDue);
    }

    // ---- 7-day net series ----

    [Fact]
    public async Task Progress_series_has_seven_days_oldest_first()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.Equal(7, dash.Progress.Days.Count);
        Assert.Equal(Today.AddDays(-6), dash.Progress.Days[0].Date);
        Assert.Equal(Today, dash.Progress.Days[^1].Date);
    }

    [Fact]
    public async Task Net_is_fuel_minus_burn_when_profile_incomplete_kindle_unavailable()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        // No profile (sex/height/dob) and no weight → kindle unavailable.
        var meal = await CaloricMealAsync(c, "meal", 800);
        await c.Consumption.LogAsync(c.User,
            new ConsumptionEntryInput(ConsumptionSource.Meal, meal.Id, Servings: 1, Date: Today), default);
        await c.Burn.LogAsync(c.User, new BurnActivityInput("run", 300, Today), default);

        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);

        Assert.False(dash.Progress.KindleAvailable);
        var todayPoint = dash.Progress.Days[^1];
        Assert.Equal(800, todayPoint.Fuel, 1);
        Assert.Equal(300, todayPoint.Burn, 1);
        Assert.Equal(0, todayPoint.Kindle, 1);
        Assert.Equal(500, todayPoint.Net, 1); // 800 − 300 − 0
        Assert.Equal(500, dash.Progress.CumulativeNet, 1);
        // Net surplus of 500 kcal → a small weight gain → negative pounds lost.
        Assert.True(dash.Progress.EstimatedWeightLost < 0);
    }

    [Fact]
    public async Task Net_subtracts_kindle_when_profile_complete()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        // Complete the profile + record a weight so kindle is computable.
        await c.Profile.UpdateProfileAsync(c.User, new ProfileInput(
            GoalType.WeightLoss, SexAtBirth.Male, 180,
            new DateOnly(1990, 1, 1), 80, WeightUnit.Kilogram), default);
        var refreshed = await c.Users.GetByIdAsync(c.User.Id, default);

        var kindle = KindleCostCalculator.TryCompute(
            SexAtBirth.Male, 180, new DateOnly(1990, 1, 1), (80, WeightUnit.Kilogram), Today);
        Assert.NotNull(kindle);

        var dash = await c.Dashboard.GetAsync(refreshed!, Today, NowEarly, default);

        Assert.True(dash.Progress.KindleAvailable);
        var todayPoint = dash.Progress.Days[^1];
        Assert.Equal(kindle!.Value, todayPoint.Kindle, 1);
        // No fuel/burn logged → net is purely the (negative) kindle for each of 7 days.
        Assert.Equal(-kindle.Value, todayPoint.Net, 1);
        Assert.Equal(-7.0 * kindle.Value, dash.Progress.CumulativeNet, 1);
        // A pure deficit → positive estimated weight lost, in the recorded unit (kg).
        Assert.True(dash.Progress.EstimatedWeightLost > 0);
        Assert.Equal(WeightUnit.Kilogram, dash.Progress.WeightUnit);
    }

    [Fact]
    public async Task Has_data_is_false_for_a_brand_new_user()
    {
        using var db = TestHelpers.NewContext();
        var c = await SetupAsync(db);
        var dash = await c.Dashboard.GetAsync(c.User, Today, NowEarly, default);
        Assert.False(dash.Progress.HasData);
    }
}
