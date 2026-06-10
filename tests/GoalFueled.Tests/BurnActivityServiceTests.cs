using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

public class BurnActivityServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(AppUser user, AppUser other, BurnService burn)> SetupAsync(GoalFueledDbContext db)
    {
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var burn = new BurnService(new BurnActivityRepository(db));
        return (user!, other!, burn);
    }

    // ---- logging / day view / totals ----

    [Fact]
    public async Task Log_records_activity_with_positive_magnitude_and_defaults_to_today()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        var activity = await burn.LogAsync(user, new BurnActivityInput("Morning run", 300), default);

        Assert.Equal("Morning run", activity.Name);
        Assert.Equal(300, activity.CaloriesBurned); // stored positive
        Assert.Equal(Today, activity.Date); // defaults to today
    }

    [Fact]
    public async Task Log_trims_the_name()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        var activity = await burn.LogAsync(user, new BurnActivityInput("  Cycling  ", 120), default);
        Assert.Equal("Cycling", activity.Name);
    }

    [Fact]
    public async Task Day_view_sums_total_calories_burned()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        await burn.LogAsync(user, new BurnActivityInput("Run", 300), default);
        await burn.LogAsync(user, new BurnActivityInput("Walk", 100), default);

        var day = await burn.GetDayAsync(user, Today, default);

        Assert.Equal(Today, day.Date);
        Assert.Equal(2, day.Activities.Count);
        Assert.Equal(400, day.TotalCaloriesBurned); // positive total — caller subtracts it
    }

    [Fact]
    public async Task Day_defaults_to_today_when_date_omitted()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);
        await burn.LogAsync(user, new BurnActivityInput("Run", 200), default);

        var day = await burn.GetDayAsync(user, date: null, default);
        Assert.Equal(Today, day.Date);
        Assert.Single(day.Activities);
    }

    [Fact]
    public async Task Activities_are_scoped_per_day()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        await burn.LogAsync(user, new BurnActivityInput("Yesterday run", 250, Today.AddDays(-1)), default);
        await burn.LogAsync(user, new BurnActivityInput("Today run", 300), default);

        var today = await burn.GetDayAsync(user, Today, default);
        var yesterday = await burn.GetDayAsync(user, Today.AddDays(-1), default);

        Assert.Single(today.Activities);
        Assert.Equal("Today run", today.Activities[0].Name);
        Assert.Single(yesterday.Activities);
        Assert.Equal("Yesterday run", yesterday.Activities[0].Name);
    }

    // ---- removal ----

    [Fact]
    public async Task Remove_deletes_the_activity()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);
        var activity = await burn.LogAsync(user, new BurnActivityInput("Run", 300), default);

        await burn.RemoveAsync(user, activity.Id, default);

        var day = await burn.GetDayAsync(user, Today, default);
        Assert.Empty(day.Activities);
    }

    // ---- per-user isolation ----

    [Fact]
    public async Task Users_cannot_see_or_remove_each_others_activities()
    {
        using var db = TestHelpers.NewContext();
        var (user, other, burn) = await SetupAsync(db);

        var mine = await burn.LogAsync(user, new BurnActivityInput("Mine", 300), default);
        await burn.LogAsync(other, new BurnActivityInput("Theirs", 200), default);

        Assert.Single((await burn.GetDayAsync(user, Today, default)).Activities);
        Assert.Single((await burn.GetDayAsync(other, Today, default)).Activities);
        await Assert.ThrowsAsync<NotFoundException>(() => burn.RemoveAsync(other, mine.Id, default));
    }

    // ---- validation ----

    [Fact]
    public async Task Validation_rejects_missing_name()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            burn.LogAsync(user, new BurnActivityInput("  ", 300), default));
    }

    [Fact]
    public async Task Validation_rejects_non_positive_and_oversized_calories()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            burn.LogAsync(user, new BurnActivityInput("Run", 0), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            burn.LogAsync(user, new BurnActivityInput("Run", -50), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            burn.LogAsync(user, new BurnActivityInput("Run", InputLimits.BurnCaloriesMax + 1), default));
    }

    [Fact]
    public async Task Validation_rejects_future_date()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() =>
            burn.LogAsync(user, new BurnActivityInput("Run", 300, Today.AddDays(1)), default));
    }

    // ---- suggestions (autocomplete) ----

    [Fact]
    public async Task Suggestions_are_distinct_by_name_with_most_recent_calories()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        // Two "Run" entries on different days; the most recent (today, 350) should win the default calories.
        await burn.LogAsync(user, new BurnActivityInput("Run", 300, Today.AddDays(-2)), default);
        await burn.LogAsync(user, new BurnActivityInput("Yoga", 120, Today.AddDays(-1)), default);
        await burn.LogAsync(user, new BurnActivityInput("Run", 350), default);

        var suggestions = await burn.GetSuggestionsAsync(user, default);

        // Distinct by name; ordered most-recent-first (Run logged today, then Yoga).
        Assert.Equal(2, suggestions.Count);
        Assert.Equal("Run", suggestions[0].Name);
        Assert.Equal(350, suggestions[0].LastCalories); // most-recent entry's calories
        Assert.Equal("Yoga", suggestions[1].Name);
        Assert.Equal(120, suggestions[1].LastCalories);
    }

    [Fact]
    public async Task Suggestions_dedupe_case_insensitively()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, burn) = await SetupAsync(db);

        await burn.LogAsync(user, new BurnActivityInput("run", 300, Today.AddDays(-1)), default);
        await burn.LogAsync(user, new BurnActivityInput("Run", 350), default);

        var suggestions = await burn.GetSuggestionsAsync(user, default);
        Assert.Single(suggestions);
        Assert.Equal("Run", suggestions[0].Name); // most-recent casing wins
        Assert.Equal(350, suggestions[0].LastCalories);
    }

    [Fact]
    public async Task Suggestions_are_per_user()
    {
        using var db = TestHelpers.NewContext();
        var (user, other, burn) = await SetupAsync(db);

        await burn.LogAsync(user, new BurnActivityInput("Mine", 300), default);
        await burn.LogAsync(other, new BurnActivityInput("Theirs", 200), default);

        var mine = await burn.GetSuggestionsAsync(user, default);
        Assert.Single(mine);
        Assert.Equal("Mine", mine[0].Name);
    }
}
