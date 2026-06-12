using GoalFueled.Api.Auth;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

public class GamificationTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(AppUser user, ConsumptionService consumption, BurnService burn,
        WeightService weights, GamificationService gamification, IUserStatsRepository statsRepo)>
        SetupAsync(GoalFueledDbContext db)
    {
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var plans = new MealPlanService(
            new MealPlanRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));
        var consumption = new ConsumptionService(
            new ConsumptionEntryRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db),
            new WeightEntryRepository(db), plans, TestHelpers.NewAwards(db));
        var burn = new BurnService(new BurnActivityRepository(db), new WeightEntryRepository(db), TestHelpers.NewAwards(db));
        var weights = new WeightService(new WeightEntryRepository(db), TestHelpers.NewAwards(db));
        var statsRepo = new UserStatsRepository(db);
        var gamification = new GamificationService(
            statsRepo, new ConsumptionEntryRepository(db), consumption, burn,
            new WeightEntryRepository(db), new GoalRepository(db));

        return (user!, consumption, burn, weights, gamification, statsRepo);
    }

    private static ConsumptionEntryInput FreeForm(string name, double kcal, DateOnly? date = null) =>
        new(ConsumptionSource.FreeForm, Name: name,
            Nutrition: new[] { new NutrientAmount { NutrientId = "calories", Value = kcal } }, Date: date);

    // ---- the level ladder ----

    [Theory]
    [InlineData(0, 1, "Ignition", 500)]
    [InlineData(499, 1, "Ignition", 500)]
    [InlineData(500, 2, "Liftoff", 1_500)]
    [InlineData(12_000, 6, "Orbit", 19_000)]
    [InlineData(19_000, 7, "Escape Velocity", 26_000)]
    [InlineData(26_500, 8, "Escape Velocity", 33_000)]
    public void Level_ladder_resolves_points(long points, int number, string rank, long next)
    {
        var level = FuelLevels.ForPoints(points);
        Assert.Equal(number, level.Number);
        Assert.Equal(rank, level.Rank);
        Assert.Equal(next, level.Next);
    }

    [Fact]
    public void Week_starts_on_monday()
    {
        var wednesday = new DateOnly(2026, 6, 10);
        Assert.Equal(new DateOnly(2026, 6, 8), FuelLevels.WeekStartOf(wednesday));
        Assert.Equal(new DateOnly(2026, 6, 8), FuelLevels.WeekStartOf(new DateOnly(2026, 6, 8))); // Monday maps to itself
        Assert.Equal(new DateOnly(2026, 6, 8), FuelLevels.WeekStartOf(new DateOnly(2026, 6, 14))); // Sunday
    }

    // ---- awards ----

    [Fact]
    public async Task Logging_fuel_awards_entry_points_and_the_full_day_bonus_once()
    {
        using var db = TestHelpers.NewContext();
        var (user, consumption, _, _, gamification, _) = await SetupAsync(db);

        await consumption.LogAsync(user, FreeForm("Breakfast", 400), default);
        var afterOne = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(GamificationAwardService.EntryPoints, afterOne.FuelPoints);

        // Second fuel entry: +10 entry points +25 full-day bonus.
        await consumption.LogAsync(user, FreeForm("Lunch", 600), default);
        var afterTwo = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(2 * GamificationAwardService.EntryPoints + GamificationAwardService.FullDayPoints, afterTwo.FuelPoints);

        // Third entry: +10 only — the full-day bonus never repeats for the same day.
        await consumption.LogAsync(user, FreeForm("Dinner", 700), default);
        var afterThree = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(3 * GamificationAwardService.EntryPoints + GamificationAwardService.FullDayPoints, afterThree.FuelPoints);
    }

    [Fact]
    public async Task Entry_points_cap_per_day()
    {
        using var db = TestHelpers.NewContext();
        var (user, consumption, _, _, gamification, _) = await SetupAsync(db);

        for (var i = 0; i < 8; i++)
            await consumption.LogAsync(user, FreeForm($"Snack {i}", 100), default);

        var summary = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(GamificationAwardService.DailyEntryPointsCap + GamificationAwardService.FullDayPoints, summary.FuelPoints);
    }

    [Fact]
    public async Task Weight_on_a_scheduled_day_awards_the_bonus_once()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, weights, gamification, _) = await SetupAsync(db);

        // A daily schedule makes today a due day.
        user.WeightScheduleCadence = WeightScheduleCadence.Daily;
        user.WeightScheduleStartDate = Today.AddDays(-7);

        await weights.RecordAsync(user, new WeightEntryInput(182, WeightUnit.Pound), default);
        var first = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(GamificationAwardService.ScheduledWeighInPoints, first.FuelPoints);

        // Updating the same day's weight does not double-award.
        await weights.RecordAsync(user, new WeightEntryInput(181.6, WeightUnit.Pound), default);
        var second = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(GamificationAwardService.ScheduledWeighInPoints, second.FuelPoints);
    }

    [Fact]
    public async Task Weight_without_a_schedule_awards_nothing()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, weights, gamification, _) = await SetupAsync(db);

        await weights.RecordAsync(user, new WeightEntryInput(182, WeightUnit.Pound), default);
        var summary = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(0, summary.FuelPoints);
    }

    // ---- weekly review ----

    [Fact]
    public async Task Review_scores_logged_week_and_completion_awards_bonus_once()
    {
        using var db = TestHelpers.NewContext();
        var (user, consumption, _, _, gamification, _) = await SetupAsync(db);

        // Fully log every day of the previous week (2 fuel entries each).
        var lastWeekStart = FuelLevels.WeekStartOf(Today).AddDays(-7);
        for (var i = 0; i < 7; i++)
        {
            var day = lastWeekStart.AddDays(i);
            await consumption.LogAsync(user, FreeForm("Breakfast", 400, day), default);
            await consumption.LogAsync(user, FreeForm("Dinner", 700, day), default);
        }

        var review = await gamification.GetReviewAsync(user, lastWeekStart, null, default);
        Assert.False(review.Completed);
        Assert.Equal(7, review.DaysFullyLogged);
        Assert.Equal(50, review.LoggingScore);
        // No kindle (profile incomplete) → adherence falls back to fully-logged days.
        Assert.Equal(30, review.AdherenceScore);
        // No weight schedule → no weigh-ins due → full weight marks.
        Assert.Equal(20, review.WeightScore);
        Assert.Equal(100, review.Score);

        var before = (await gamification.GetSummaryAsync(user, null, default)).FuelPoints;
        var completed = await gamification.CompleteReviewAsync(user, new CompleteReviewInput(lastWeekStart), null, default);
        Assert.True(completed.Completed);
        Assert.Equal(GamificationService.ReviewBasePoints + 100, completed.CompletionBonus);

        var after = (await gamification.GetSummaryAsync(user, null, default)).FuelPoints;
        Assert.Equal(before + GamificationService.ReviewBasePoints + 100, after);

        // Completing again is idempotent — no double bonus.
        await gamification.CompleteReviewAsync(user, new CompleteReviewInput(lastWeekStart), null, default);
        Assert.Equal(after, (await gamification.GetSummaryAsync(user, null, default)).FuelPoints);
    }

    [Fact]
    public async Task Current_week_cannot_be_completed()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, _, gamification, _) = await SetupAsync(db);

        await Assert.ThrowsAsync<GoalFueled.Api.Common.ValidationException>(() =>
            gamification.CompleteReviewAsync(user, new CompleteReviewInput(FuelLevels.WeekStartOf(Today)), null, default));
    }

    [Fact]
    public async Task Summary_reports_pending_review_for_last_finished_week()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, _, gamification, _) = await SetupAsync(db);

        var summary = await gamification.GetSummaryAsync(user, null, default);
        Assert.Equal(FuelLevels.WeekStartOf(Today).AddDays(-7), summary.PendingReviewWeekStart);

        await gamification.CompleteReviewAsync(
            user, new CompleteReviewInput(summary.PendingReviewWeekStart!.Value), null, default);
        var after = await gamification.GetSummaryAsync(user, null, default);
        Assert.Null(after.PendingReviewWeekStart);
        Assert.NotNull(after.LastReview);
    }
}
