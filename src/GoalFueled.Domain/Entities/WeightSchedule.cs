namespace GoalFueled.Domain.Entities;

/// <summary>
/// A user's recurring weight-measurement schedule. Decides, for any given calendar day, whether a weight
/// measurement is "due" — which is what surfaces the recurring "Log your weight" task in The Log.
///
/// <para>The schedule is anchored on a <see cref="StartDate"/>: its <see cref="DateOnly.DayOfWeek"/> is the
/// chosen "day of the week to start measurements", and it also serves as the recurrence anchor for the
/// "every N" cadences. <see cref="Interval"/> is the N for <see cref="WeightScheduleCadence.EveryNDays"/>
/// and <see cref="WeightScheduleCadence.EveryNWeeks"/> (and is ignored for the others).</para>
///
/// <para>This is a pure value object — it holds no persistence concerns and exposes the due-date rule via
/// <see cref="IsDueOn"/> so the rule is trivially unit-testable. Per-user schedule state lives as scalars
/// on <see cref="AppUser"/>; <see cref="FromUser"/>/<see cref="AppUser.HasWeightSchedule"/> bridge the two.</para>
/// </summary>
public sealed record WeightSchedule(
    WeightScheduleCadence Cadence,
    DateOnly StartDate,
    int Interval = 1)
{
    /// <summary>The day-of-week the schedule starts on (and recurs on for the weekly cadences).</summary>
    public DayOfWeek StartDayOfWeek => StartDate.DayOfWeek;

    /// <summary>
    /// Whether a weight measurement is due on <paramref name="date"/>. A measurement is never due before
    /// the schedule's <see cref="StartDate"/>. The rule per cadence:
    /// <list type="bullet">
    /// <item><see cref="WeightScheduleCadence.Daily"/> — due every day (on/after the start).</item>
    /// <item><see cref="WeightScheduleCadence.Weekly"/> — due on the start's day-of-week each week.</item>
    /// <item><see cref="WeightScheduleCadence.EveryNDays"/> — due on the start date and every Nth day after it.</item>
    /// <item><see cref="WeightScheduleCadence.EveryNWeeks"/> — due on the start's day-of-week every Nth week
    ///   from the start (so day-of-week must match AND the whole-week offset must be a multiple of N).</item>
    /// </list>
    /// </summary>
    public bool IsDueOn(DateOnly date)
    {
        if (date < StartDate) return false;

        return Cadence switch
        {
            WeightScheduleCadence.Daily => true,
            WeightScheduleCadence.Weekly => date.DayOfWeek == StartDate.DayOfWeek,
            WeightScheduleCadence.EveryNDays => DivisibleByInterval(date.DayNumber - StartDate.DayNumber),
            WeightScheduleCadence.EveryNWeeks =>
                date.DayOfWeek == StartDate.DayOfWeek
                && DivisibleByInterval((date.DayNumber - StartDate.DayNumber) / 7),
            _ => false,
        };
    }

    private bool DivisibleByInterval(int offset)
    {
        var n = Interval <= 0 ? 1 : Interval;
        return offset % n == 0;
    }

    /// <summary>
    /// Reconstructs the schedule from a user's persisted scalars, or null when the user has no schedule
    /// configured (no cadence / no start date).
    /// </summary>
    public static WeightSchedule? FromUser(AppUser user) =>
        user.WeightScheduleCadence is { } cadence && user.WeightScheduleStartDate is { } start
            ? new WeightSchedule(cadence, start, user.WeightScheduleInterval ?? 1)
            : null;
}
