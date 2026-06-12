using GoalFueled.Domain;

namespace GoalFueled.Domain.Contracts;

/// <summary>
/// The gamification snapshot for the signed-in user (DESIGN.md §4): lifetime Fuel Points, the
/// derived level/rank, the current week's running score, and the state of the weekly review loop.
/// </summary>
public sealed record GamificationSummaryDto(
    /// <summary>Lifetime Fuel Points.</summary>
    long FuelPoints,
    /// <summary>The level derived from <see cref="FuelPoints"/>.</summary>
    LevelDto Level,
    /// <summary>Monday of the current (in-progress) week.</summary>
    DateOnly CurrentWeekStart,
    /// <summary>The running 0–100 score for the current week so far (days elapsed only).</summary>
    int CurrentWeekScore,
    /// <summary>Monday of the most recent finished week when it has not been reviewed yet; null once reviewed.</summary>
    DateOnly? PendingReviewWeekStart,
    /// <summary>The most recently completed review, or null when none exist.</summary>
    CompletedReviewDto? LastReview);

/// <summary>A level on the fuel-rank ladder (IGNITION → LIFTOFF → … ).</summary>
public sealed record LevelDto(
    /// <summary>1-based level number.</summary>
    int Number,
    /// <summary>Rank name, e.g. "Cruise".</summary>
    string Rank,
    /// <summary>Cumulative Fuel Points at which this level starts.</summary>
    long FloorPoints,
    /// <summary>Cumulative Fuel Points needed for the next level (null only if uncapped logic ever ends).</summary>
    long NextLevelPoints);

/// <summary>A completed weekly review in summary form.</summary>
public sealed record CompletedReviewDto(DateOnly WeekStart, int Score, long PointsAwarded, DateTimeOffset CompletedAt);

/// <summary>
/// The full "your week in fuel" review (DESIGN.md §3.3) for one week. Score components reflect
/// engagement (logging consistency 50, budget adherence 30, weigh-ins on schedule 20) — never
/// deficit size.
/// </summary>
public sealed record WeeklyReviewDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    /// <summary>Whether the user has completed (claimed) this review.</summary>
    bool Completed,
    DateTimeOffset? CompletedAt,
    /// <summary>Total 0–100 score.</summary>
    int Score,
    /// <summary>Logging-consistency component (0–50).</summary>
    int LoggingScore,
    /// <summary>Budget-adherence component (0–30).</summary>
    int AdherenceScore,
    /// <summary>Weigh-in component (0–20; full marks when none were due).</summary>
    int WeightScore,
    /// <summary>Days with at least two fuel entries logged.</summary>
    int DaysFullyLogged,
    int DaysAdherent,
    int WeighInsDue,
    int WeighInsLogged,
    double TotalFuel,
    int TotalBurn,
    /// <summary>Week net = fuel − burn − kindle (0 kindle when unavailable).</summary>
    double NetCalories,
    bool KindleAvailable,
    /// <summary>Per-day breakdown, oldest first.</summary>
    IReadOnlyList<ReviewDayDto> Days,
    /// <summary>The day that best matched the budget (null when nothing was logged).</summary>
    DateOnly? BestDay,
    /// <summary>Weight change across the week (last in-week entry − last entry at/before week start), in <see cref="WeightDeltaUnit"/>.</summary>
    double? WeightDelta,
    WeightUnit? WeightDeltaUnit,
    /// <summary>Fuel Points earned from daily awards inside this week (best effort from the rolling ledger).</summary>
    long PointsEarned,
    /// <summary>Points granted when the review was completed (base + score bonus); null until completed.</summary>
    long? CompletionBonus);

/// <summary>One day inside a weekly review.</summary>
public sealed record ReviewDayDto(
    DateOnly Date,
    double Fuel,
    int Burn,
    double Net,
    int FuelEntries,
    bool FullyLogged,
    bool Adherent,
    bool WeighInDue,
    bool WeighInLogged);

/// <summary>Request body for completing a weekly review.</summary>
public sealed record CompleteReviewInput(DateOnly WeekStart);
