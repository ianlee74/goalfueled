using GoalFueled.Domain;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// A goal as returned to clients, including its definition and a freshly-computed
/// <see cref="GoalProgressDto"/> snapshot that drives the progress visual.
/// </summary>
public sealed record GoalDto(
    string Id,
    string Name,
    GoalMetricType MetricType,
    /// <summary>Catalog nutrient id for nutrient goals; null for weight goals.</summary>
    string? NutrientId,
    /// <summary>Resolved nutrient display name (e.g. "Vitamin C") for nutrient goals; null otherwise.</summary>
    string? NutrientName,
    /// <summary>Unit label for the metric (e.g. "lb", "kg", "kcal", "g", "mg").</summary>
    string MetricUnit,
    double TargetValue,
    GoalCadence Cadence,
    DateOnly StartDate,
    DateOnly? TargetDate,
    int? RecurrenceDays,
    bool ExceedingIsPositive,
    GoalProgressDto Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>How a goal's outcome stands relative to its target.</summary>
public enum GoalStatus
{
    /// <summary>Underway and not yet at the target.</summary>
    InProgress = 0,

    /// <summary>The target has been met (and possibly exceeded in the positive direction).</summary>
    Achieved = 1,

    /// <summary>The goal's deadline/period has passed without meeting the target.</summary>
    Missed = 2,

    /// <summary>Not enough data yet to compute progress (e.g. no weight recorded, no meal plan).</summary>
    NoData = 3
}

/// <summary>
/// A computed progress snapshot for a goal, shaped for direct use by the progress visual.
/// </summary>
/// <param name="Status">Overall standing (in-progress / achieved / missed / no data).</param>
/// <param name="BaselineValue">
/// Starting point the progress is measured from. For weight goals this is the earliest recorded weight
/// at or after the start date; for nutrient goals it is zero (intake accumulates from nothing).
/// </param>
/// <param name="CurrentValue">The latest measured value of the metric.</param>
/// <param name="TargetValue">The target the user is aiming for (echoes the goal's target).</param>
/// <param name="Percent">
/// Progress from baseline toward target, clamped to 0..100 for the bar. Reaches 100 when the target is met.
/// </param>
/// <param name="ExceededTarget">True when the current value has passed the target.</param>
/// <param name="OutcomePositive">
/// Whether the current standing is a good thing, combining direction-of-travel with
/// <c>ExceedingIsPositive</c> — drives whether the visual celebrates or warns.
/// </param>
/// <param name="PeriodLabel">Human label for the active period (e.g. "by Dec 31, 2026" or "every 7 days").</param>
/// <param name="Message">A short, friendly status message for the visual to display.</param>
public sealed record GoalProgressDto(
    GoalStatus Status,
    double BaselineValue,
    double CurrentValue,
    double TargetValue,
    double Percent,
    bool ExceededTarget,
    bool OutcomePositive,
    string PeriodLabel,
    string Message);

/// <summary>Payload for creating or updating a goal.</summary>
public sealed record GoalInput(
    string Name,
    GoalMetricType MetricType,
    double TargetValue,
    GoalCadence Cadence,
    bool ExceedingIsPositive,
    /// <summary>Required for nutrient goals: a catalog nutrient id. Ignored for weight goals.</summary>
    string? NutrientId = null,
    /// <summary>Defaults to today (the request date) when omitted.</summary>
    DateOnly? StartDate = null,
    /// <summary>Required for one-time goals: the deadline. Ignored for recurring goals.</summary>
    DateOnly? TargetDate = null,
    /// <summary>Required for recurring goals: the period length in days. Ignored for one-time goals.</summary>
    int? RecurrenceDays = null);
