using GoalFueled.Domain;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// The aggregated payload for the landing-page dashboard. Composed server-side from the meal-plan,
/// consumption, burn, weight and user services so the client makes a single call. Every piece tolerates
/// missing data (no active plan, incomplete profile, nothing logged) via its own nullable/empty shape, so
/// the client can render friendly zero-states without extra round-trips.
/// </summary>
public sealed record DashboardDto(
    /// <summary>The day the dashboard was computed for, "yyyy-MM-dd" (the caller's request date / today).</summary>
    DateOnly Date,
    /// <summary>The active meal plan's id, or null when the user is not following any plan.</summary>
    string? ActivePlanId,
    /// <summary>The active meal plan's name, or null when none is selected (drives the "pick a plan" prompt).</summary>
    string? ActivePlanName,
    /// <summary>
    /// The next (up to) three upcoming, not-yet-completed planned meals, ordered by their scheduled time
    /// starting from now and rolling into subsequent plan days when today has fewer than three left.
    /// </summary>
    IReadOnlyList<UpcomingMealDto> UpcomingMeals,
    /// <summary>The "log your weight today" prompt, present only when a measurement is due today; null otherwise.</summary>
    WeightDueDto? WeightDue,
    /// <summary>The 7-day fuel/burn/kindle/net series plus the derived weight-change estimate.</summary>
    ProgressSeriesDto Progress);

/// <summary>
/// One upcoming planned meal on the dashboard. Carries everything the client needs to show it and to mark
/// it complete via the existing <c>POST /api/consumption/planned/complete</c> endpoint: the task
/// <see cref="Ref"/> and the <see cref="Date"/> it falls on.
/// </summary>
public sealed record UpcomingMealDto(
    /// <summary>Stable correlation key for completing this task (same form as <see cref="PlannedMealTaskDto.Ref"/>).</summary>
    string Ref,
    string MealId,
    /// <summary>Resolved meal name (null if the meal is no longer visible/exists).</summary>
    string? MealName,
    /// <summary>The calendar day this occurrence falls on, "yyyy-MM-dd" (today or a later plan day).</summary>
    DateOnly Date,
    /// <summary>Scheduled time of day, "HH:mm:ss"; null for an untimed/"anytime" meal.</summary>
    TimeOnly? Time,
    /// <summary>Calories for this meal (from the same meal-total roll-up the plan summary uses).</summary>
    double Calories,
    /// <summary>True when this occurrence is on a day later than today (it rolled forward).</summary>
    bool IsLaterDay);

/// <summary>
/// The "weight is due today" prompt. Present in the dashboard only when the user's weight schedule makes a
/// measurement due today. <see cref="AlreadyRecorded"/> mirrors the one-per-day weight model so the client
/// can show "logged" instead of the input once a value exists for today.
/// </summary>
public sealed record WeightDueDto(
    bool AlreadyRecorded,
    /// <summary>Today's recorded weight value when already recorded; null otherwise.</summary>
    double? RecordedValue,
    /// <summary>Today's recorded weight unit when already recorded; null otherwise.</summary>
    WeightUnit? RecordedUnit);

/// <summary>
/// The 7-day progress series for fuel (eaten), burn (exercise), kindle (passive BMR cost) and the net
/// (fuel − burn − kindle), one point per day, oldest first. <see cref="EstimatedPoundsLost"/> is derived
/// from the cumulative net via the 3,500-kcal-per-pound rule (deficit → positive pounds lost).
/// </summary>
public sealed record ProgressSeriesDto(
    IReadOnlyList<ProgressDayDto> Days,
    /// <summary>Sum of every day's net (fuel − burn − kindle), in kcal. Negative = net deficit.</summary>
    double CumulativeNet,
    /// <summary>Estimated body weight lost over the window (deficit → positive), in the user's unit.</summary>
    double EstimatedWeightLost,
    /// <summary>The unit <see cref="EstimatedWeightLost"/> is expressed in (the user's last-used unit).</summary>
    WeightUnit WeightUnit,
    /// <summary>
    /// True when the kindle cost could be computed for the window (profile complete + a recorded weight);
    /// when false the net is fuel − burn only and the client shows a "complete your profile" nudge.
    /// </summary>
    bool KindleAvailable,
    /// <summary>True when the window has no fuel, burn or kindle data at all (drives the zero-state).</summary>
    bool HasData);

/// <summary>One day's point in the 7-day progress series.</summary>
public sealed record ProgressDayDto(
    /// <summary>The calendar day, "yyyy-MM-dd".</summary>
    DateOnly Date,
    /// <summary>Calories eaten (fuel) that day.</summary>
    double Fuel,
    /// <summary>Calories burned via logged activity (burn) that day, a positive magnitude.</summary>
    double Burn,
    /// <summary>Passive sedentary cost (kindle) that day; 0 when the profile is incomplete.</summary>
    double Kindle,
    /// <summary>Net = fuel − burn − kindle for the day, in kcal.</summary>
    double Net);
