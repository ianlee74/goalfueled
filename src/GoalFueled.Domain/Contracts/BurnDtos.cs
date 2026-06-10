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
    DateTimeOffset UpdatedAt,
    /// <summary>
    /// Optional user-specified time of day, serialized as "HH:mm:ss". Drives the entry's placement in The
    /// Log timeline; null for older entries (clients fall back to <see cref="CreatedAt"/>).
    /// </summary>
    TimeOnly? Time = null);

/// <summary>
/// A day's burn log: its activities plus the day's total calories burned (a positive magnitude — callers
/// subtract it when computing a net-calorie summary).
/// </summary>
public sealed record BurnDayDto(
    /// <summary>The day this log is for, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    IReadOnlyList<BurnActivityDto> Activities,
    /// <summary>Sum of every activity's <see cref="BurnActivityDto.CaloriesBurned"/> for the day.</summary>
    int TotalCaloriesBurned,
    /// <summary>
    /// The passive "Kindle" cost (kcal, a positive magnitude): the user's daily sedentary calorie usage,
    /// derived on the fly from their profile (sex, date of birth → age, height) and latest recorded weight
    /// via Mifflin–St Jeor × 1.2. Null when any required input is missing (the client then prompts the user
    /// to complete their profile instead of showing a wrong/zero number). Not persisted as a burn activity —
    /// it is computed per request so it always reflects the current weight. A net-calorie summary subtracts
    /// it: net = fuel − burn − kindle.
    /// </summary>
    int? KindleCost = null);

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
    DateOnly? Date = null,
    /// <summary>
    /// Optional time of day the activity occurred ("HH:mm:ss"). When omitted, the entry has no specified
    /// time and the client falls back to its creation time for timeline ordering/display.
    /// </summary>
    TimeOnly? Time = null);
