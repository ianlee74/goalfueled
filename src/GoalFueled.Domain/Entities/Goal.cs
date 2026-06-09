namespace GoalFueled.Domain.Entities;

/// <summary>
/// A target the user sets for a tracked metric and works toward. Goals are strictly per-user: each is
/// owned by, and only visible to, the user who created it (like <see cref="WeightEntry"/>).
///
/// A goal targets either the user's body weight (<see cref="GoalMetricType.Weight"/>) or a single
/// nutrient from the catalog (<see cref="GoalMetricType.Nutrient"/>, keyed by <see cref="NutrientId"/>).
/// It is either <b>one-time</b> — reach <see cref="TargetValue"/> by <see cref="TargetDate"/> (e.g.
/// "lose 10 lbs by 12/31/2026") — or <b>recurring</b> — hit <see cref="TargetValue"/> every
/// <see cref="RecurrenceDays"/> days starting <see cref="StartDate"/> (e.g. "consume 2000 calories every
/// 7 days"). <see cref="ExceedingIsPositive"/> records whether going past the target is celebrated
/// (e.g. losing more weight than planned) or flagged (e.g. eating more calories than budgeted).
/// </summary>
public class Goal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user who created (and owns) this goal.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>Short user-facing label (e.g. "Summer cut").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the goal targets weight or a nutritional metric.</summary>
    public GoalMetricType MetricType { get; set; } = GoalMetricType.Weight;

    /// <summary>
    /// For <see cref="GoalMetricType.Nutrient"/> goals, the catalog nutrient id (e.g. "calories",
    /// "protein"). Null/ignored for weight goals.
    /// </summary>
    public string? NutrientId { get; set; }

    /// <summary>The number the user is aiming for, in the metric's natural unit (lb/kg or the nutrient unit).</summary>
    public double TargetValue { get; set; }

    /// <summary>Whether the goal is achieved once or repeats on a fixed cadence.</summary>
    public GoalCadence Cadence { get; set; } = GoalCadence.OneTime;

    /// <summary>
    /// The day the goal begins. For a one-time weight goal this anchors the starting measurement; for a
    /// recurring goal it anchors the first period.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>The deadline for a one-time goal. Null for recurring goals.</summary>
    public DateOnly? TargetDate { get; set; }

    /// <summary>The length of each period in days for a recurring goal (e.g. 7). Null for one-time goals.</summary>
    public int? RecurrenceDays { get; set; }

    /// <summary>
    /// True when overshooting the target counts as a win (e.g. losing more weight, or hitting more
    /// protein than planned); false when overshooting is bad (e.g. exceeding a calorie budget).
    /// </summary>
    public bool ExceedingIsPositive { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
