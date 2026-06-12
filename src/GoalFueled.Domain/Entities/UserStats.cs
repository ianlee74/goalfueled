namespace GoalFueled.Domain.Entities;

/// <summary>
/// Per-user gamification state (DESIGN.md §4): the lifetime Fuel Point total, a small rolling
/// ledger of per-day awards (so daily caps and one-shot bonuses are enforceable), and the
/// completed weekly reviews. Strictly per-user data; exactly one document per user.
///
/// <para><b>Safety rule:</b> points are awarded only for behaviors (logging, full days, scheduled
/// weigh-ins, completing reviews) — never for outcomes like deficit size or weight lost, so the
/// system cannot reward under-eating.</para>
/// </summary>
public class UserStats
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user this stats document belongs to (one per user).</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>Lifetime Fuel Points. Points only ever accrue (no clawbacks on deletes).</summary>
    public long FuelPoints { get; set; }

    /// <summary>
    /// Rolling per-day award bookkeeping, pruned to a recent window on write. Old days beyond the
    /// window can no longer earn retroactive bonuses, which keeps the document small.
    /// </summary>
    public List<DailyAward> DailyAwards { get; set; } = new();

    /// <summary>Completed weekly reviews (one per week start), newest data appended last.</summary>
    public List<WeeklyReviewRecord> WeeklyReviews { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One calendar day's award bookkeeping (owned by <see cref="UserStats"/>).</summary>
public class DailyAward
{
    public DateOnly Date { get; set; }

    /// <summary>Points granted for individual fuel/burn entries this day (capped per day).</summary>
    public int EntryPoints { get; set; }

    /// <summary>Whether the "full day logged" bonus (≥2 fuel entries) has been granted.</summary>
    public bool FullDayAwarded { get; set; }

    /// <summary>Whether the "weighed in on schedule" bonus has been granted.</summary>
    public bool WeightAwarded { get; set; }
}

/// <summary>A completed weekly review (owned by <see cref="UserStats"/>).</summary>
public class WeeklyReviewRecord
{
    /// <summary>The Monday the reviewed week starts on.</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>The 0–100 week score at completion time.</summary>
    public int Score { get; set; }

    /// <summary>Fuel Points granted for completing the review (base + score bonus).</summary>
    public long PointsAwarded { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}
