using GoalFueled.Domain;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

/// <summary>
/// Unit tests for the pure "is a measurement due on date D?" rule across all four cadences, including
/// start-day anchoring and N&gt;1 intervals. The schedule's start date is a fixed Wednesday (2026-06-10)
/// unless a test states otherwise.
/// </summary>
public class WeightScheduleTests
{
    // 2026-06-10 is a Wednesday — a convenient, dependency-free anchor.
    private static readonly DateOnly Wed = new(2026, 6, 10);

    [Fact]
    public void Start_date_day_of_week_is_exposed()
    {
        var s = new WeightSchedule(WeightScheduleCadence.Weekly, Wed);
        Assert.Equal(DayOfWeek.Wednesday, s.StartDayOfWeek);
    }

    [Fact]
    public void Nothing_is_due_before_the_start_date()
    {
        var s = new WeightSchedule(WeightScheduleCadence.Daily, Wed);
        Assert.False(s.IsDueOn(Wed.AddDays(-1)));
        Assert.True(s.IsDueOn(Wed)); // due on the start itself
    }

    // ---- Daily ----

    [Fact]
    public void Daily_is_due_every_day_from_the_start()
    {
        var s = new WeightSchedule(WeightScheduleCadence.Daily, Wed);
        for (var i = 0; i < 10; i++)
            Assert.True(s.IsDueOn(Wed.AddDays(i)));
    }

    // ---- Weekly ----

    [Fact]
    public void Weekly_is_due_only_on_the_start_day_of_week()
    {
        var s = new WeightSchedule(WeightScheduleCadence.Weekly, Wed);
        Assert.True(s.IsDueOn(Wed));               // the anchor Wednesday
        Assert.True(s.IsDueOn(Wed.AddDays(7)));    // next Wednesday
        Assert.True(s.IsDueOn(Wed.AddDays(70)));   // ten weeks later
        Assert.False(s.IsDueOn(Wed.AddDays(1)));   // Thursday
        Assert.False(s.IsDueOn(Wed.AddDays(6)));   // Tuesday
    }

    // ---- Every N days ----

    [Fact]
    public void Every_n_days_is_due_on_start_and_every_nth_day()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNDays, Wed, Interval: 3);
        Assert.True(s.IsDueOn(Wed));             // day 0
        Assert.False(s.IsDueOn(Wed.AddDays(1)));
        Assert.False(s.IsDueOn(Wed.AddDays(2)));
        Assert.True(s.IsDueOn(Wed.AddDays(3)));  // day 3
        Assert.True(s.IsDueOn(Wed.AddDays(6)));  // day 6
        Assert.False(s.IsDueOn(Wed.AddDays(7)));
    }

    [Fact]
    public void Every_n_days_with_interval_one_is_daily()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNDays, Wed, Interval: 1);
        for (var i = 0; i < 5; i++)
            Assert.True(s.IsDueOn(Wed.AddDays(i)));
    }

    // ---- Every N weeks ----

    [Fact]
    public void Every_n_weeks_is_due_on_start_day_of_week_every_nth_week()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNWeeks, Wed, Interval: 2);
        Assert.True(s.IsDueOn(Wed));              // week 0, Wednesday
        Assert.False(s.IsDueOn(Wed.AddDays(7)));  // week 1, Wednesday — skipped
        Assert.True(s.IsDueOn(Wed.AddDays(14)));  // week 2, Wednesday
        Assert.True(s.IsDueOn(Wed.AddDays(28)));  // week 4, Wednesday
    }

    [Fact]
    public void Every_n_weeks_requires_the_matching_day_of_week()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNWeeks, Wed, Interval: 2);
        // A due week (week 2) but a non-matching day-of-week is not due.
        Assert.False(s.IsDueOn(Wed.AddDays(15))); // week 2, Thursday
        Assert.False(s.IsDueOn(Wed.AddDays(13))); // week 1, Tuesday
    }

    [Fact]
    public void Every_n_weeks_with_interval_one_is_weekly()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNWeeks, Wed, Interval: 1);
        Assert.True(s.IsDueOn(Wed));
        Assert.True(s.IsDueOn(Wed.AddDays(7)));
        Assert.True(s.IsDueOn(Wed.AddDays(14)));
        Assert.False(s.IsDueOn(Wed.AddDays(1)));
    }

    [Fact]
    public void Non_positive_interval_is_treated_as_one()
    {
        var s = new WeightSchedule(WeightScheduleCadence.EveryNDays, Wed, Interval: 0);
        Assert.True(s.IsDueOn(Wed.AddDays(1))); // no divide-by-zero; behaves daily
    }

    // ---- FromUser bridge ----

    [Fact]
    public void FromUser_returns_null_when_no_schedule_configured()
    {
        Assert.Null(WeightSchedule.FromUser(new AppUser()));
    }

    [Fact]
    public void FromUser_reconstitutes_persisted_scalars()
    {
        var user = new AppUser
        {
            WeightScheduleCadence = WeightScheduleCadence.EveryNWeeks,
            WeightScheduleStartDate = Wed,
            WeightScheduleInterval = 3,
        };
        var s = WeightSchedule.FromUser(user);
        Assert.NotNull(s);
        Assert.Equal(WeightScheduleCadence.EveryNWeeks, s!.Cadence);
        Assert.Equal(Wed, s.StartDate);
        Assert.Equal(3, s.Interval);
    }

    [Fact]
    public void FromUser_defaults_missing_interval_to_one()
    {
        var user = new AppUser
        {
            WeightScheduleCadence = WeightScheduleCadence.Weekly,
            WeightScheduleStartDate = Wed,
            WeightScheduleInterval = null,
        };
        Assert.Equal(1, WeightSchedule.FromUser(user)!.Interval);
    }
}
