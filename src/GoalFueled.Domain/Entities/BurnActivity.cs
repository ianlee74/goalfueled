namespace GoalFueled.Domain.Entities;

/// <summary>
/// A single logged exercise/activity that <em>burned</em> calories, recorded in the user's personal daily
/// log. Burn is the counterpart to consumption (fuel): consumption <em>adds</em> calories, burn
/// <em>removes</em> them. Burn is strictly per-user data — each activity is owned by, and only visible to,
/// the user who logged it (no app-level or cross-user view, even for admins, mirroring
/// <see cref="ConsumptionEntry"/> / <see cref="WeightEntry"/>).
///
/// <para>Like consumption there is no per-day uniqueness — a day may have many activities (a run, then a
/// walk), so logging always appends a new entry and removal is by id.</para>
///
/// <para><b>Sign convention:</b> <see cref="CaloriesBurned"/> is stored as a positive magnitude (the
/// calories the activity consumed). The negative sign is applied only where burn participates in a
/// net-calorie summary (net = fuel − burn); the stored value itself is never negative.</para>
/// </summary>
public class BurnActivity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user who logged (and owns) this activity.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>The calendar day the activity was performed on.</summary>
    public DateOnly Date { get; set; }

    /// <summary>A free-form, human-readable name for the activity (e.g. "Morning run").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The calories the activity burned, stored as a positive magnitude. Treated as negative in any
    /// net-calorie summary (see the type remarks for the sign convention).
    /// </summary>
    public int CaloriesBurned { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
