namespace GoalFueled.Domain.Entities;

/// <summary>
/// A single dated weight measurement for a user. Weight is per-user data: each entry is owned by and
/// only visible to the user who recorded it. At most one entry exists per user per calendar day —
/// recording again for the same day updates that day's entry rather than creating a duplicate.
/// </summary>
public class WeightEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user who recorded (and owns) this entry.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>The calendar day the measurement is for (the per-user uniqueness key).</summary>
    public DateOnly Date { get; set; }

    /// <summary>The measured weight value, expressed in <see cref="Unit"/>.</summary>
    public double Value { get; set; }

    /// <summary>The unit the value is expressed in (pounds or kilograms).</summary>
    public WeightUnit Unit { get; set; } = WeightUnit.Pound;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
