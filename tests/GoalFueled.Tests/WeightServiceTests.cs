using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

public class WeightServiceTests
{
    private static async Task<(AppUser user, AppUser other, WeightService weights)> SetupAsync(GoalFueledDbContext db)
    {
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var weights = new WeightService(new WeightEntryRepository(db));
        return (user!, other!, weights);
    }

    private static WeightEntryInput Entry(double value, WeightUnit unit = WeightUnit.Pound, DateOnly? date = null) =>
        new(value, unit, date);

    [Fact]
    public async Task Record_creates_entry_owned_by_user()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights) = await SetupAsync(db);

        var saved = await weights.RecordAsync(user, Entry(180, WeightUnit.Pound), default);

        Assert.Equal(180, saved.Value);
        Assert.Equal(WeightUnit.Pound, saved.Unit);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), saved.Date);
    }

    [Fact]
    public async Task Recording_again_same_day_updates_rather_than_duplicates()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights) = await SetupAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await weights.RecordAsync(user, Entry(180, WeightUnit.Pound, today), default);
        var second = await weights.RecordAsync(user, Entry(178.5, WeightUnit.Pound, today), default);

        Assert.Equal(first.Id, second.Id); // same entry updated in place
        Assert.Equal(178.5, second.Value);

        var history = await weights.ListAsync(user, default);
        Assert.Single(history); // no duplicate created
        Assert.Equal(178.5, history[0].Value);
    }

    [Fact]
    public async Task Different_days_create_separate_entries_ordered_recent_first()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights) = await SetupAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await weights.RecordAsync(user, Entry(182, WeightUnit.Pound, today.AddDays(-2)), default);
        await weights.RecordAsync(user, Entry(181, WeightUnit.Pound, today.AddDays(-1)), default);
        await weights.RecordAsync(user, Entry(180, WeightUnit.Pound, today), default);

        var history = await weights.ListAsync(user, default);
        Assert.Equal(3, history.Count);
        // Most recent first.
        Assert.Equal(today, history[0].Date);
        Assert.Equal(today.AddDays(-1), history[1].Date);
        Assert.Equal(today.AddDays(-2), history[2].Date);
    }

    [Fact]
    public async Task Date_defaults_to_today_when_omitted()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights) = await SetupAsync(db);

        var saved = await weights.RecordAsync(user, Entry(75, WeightUnit.Kilogram), default);

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), saved.Date);
        Assert.Equal(WeightUnit.Kilogram, saved.Unit);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_entries()
    {
        using var db = TestHelpers.NewContext();
        var (user, other, weights) = await SetupAsync(db);

        await weights.RecordAsync(user, Entry(180), default);
        await weights.RecordAsync(other, Entry(200), default);

        var mine = await weights.ListAsync(user, default);
        var theirs = await weights.ListAsync(other, default);

        Assert.Single(mine);
        Assert.Equal(180, mine[0].Value);
        Assert.Single(theirs);
        Assert.Equal(200, theirs[0].Value);
    }

    [Fact]
    public async Task Same_day_record_is_scoped_per_user()
    {
        using var db = TestHelpers.NewContext();
        var (user, other, weights) = await SetupAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Both record for today; each should get their own entry (no cross-user upsert collision).
        await weights.RecordAsync(user, Entry(180, WeightUnit.Pound, today), default);
        await weights.RecordAsync(other, Entry(200, WeightUnit.Pound, today), default);

        Assert.Single(await weights.ListAsync(user, default));
        Assert.Single(await weights.ListAsync(other, default));
    }

    [Fact]
    public async Task Validation_rejects_non_positive_and_oversized_values()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => weights.RecordAsync(user, Entry(0), default));
        await Assert.ThrowsAsync<ValidationException>(() => weights.RecordAsync(user, Entry(-5), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            weights.RecordAsync(user, Entry(InputLimits.WeightValueMax + 1), default));
    }
}
