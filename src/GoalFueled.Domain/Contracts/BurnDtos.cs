namespace GoalFueled.Domain.Contracts;

/// <summary>A single logged burn activity as returned to clients.</summary>
public sealed record BurnActivityDto(
    string Id,
    /// <summary>Calendar day of the activity, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    string Name,
    /// <summary>Calories burned, a positive magnitude (treated as negative in net-calorie summaries).</summary>
    int CaloriesBurned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A day's burn log: its activities plus the day's total calories burned (a positive magnitude — callers
/// subtract it when computing a net-calorie summary).
/// </summary>
public sealed record BurnDayDto(
    /// <summary>The day this log is for, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    IReadOnlyList<BurnActivityDto> Activities,
    /// <summary>Sum of every activity's <see cref="BurnActivityDto.CaloriesBurned"/> for the day.</summary>
    int TotalCaloriesBurned);

/// <summary>
/// One past activity name the user has logged, with the calories from its most-recent entry. Drives the
/// burn-name autocomplete: selecting a suggestion pre-fills the calories from that name's last entry.
/// </summary>
public sealed record BurnSuggestionDto(
    string Name,
    /// <summary>Calories from the most-recent entry that used this name.</summary>
    int LastCalories);

/// <summary>
/// Payload for logging a burn activity. The date is optional and defaults to today (the request date).
/// </summary>
public sealed record BurnActivityInput(
    /// <summary>The activity name (required).</summary>
    string Name,
    /// <summary>Calories burned, a positive magnitude (must be greater than zero).</summary>
    int CaloriesBurned,
    /// <summary>Defaults to today (the request date) when omitted.</summary>
    DateOnly? Date = null);
