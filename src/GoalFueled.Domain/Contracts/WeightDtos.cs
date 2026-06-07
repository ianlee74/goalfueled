using GoalFueled.Domain;

namespace GoalFueled.Domain.Contracts;

/// <summary>A recorded weight measurement as returned to clients.</summary>
public sealed record WeightEntryDto(
    string Id,
    /// <summary>Calendar day of the measurement, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    double Value,
    WeightUnit Unit,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Payload for recording a weight. The date is optional and defaults to today (the caller's request
/// date); recording again for an existing date updates that day's entry rather than creating a duplicate.
/// </summary>
public sealed record WeightEntryInput(
    double Value,
    WeightUnit Unit,
    DateOnly? Date = null);
