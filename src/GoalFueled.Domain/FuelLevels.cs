namespace GoalFueled.Domain;

/// <summary>
/// The Fuel Point level ladder (DESIGN.md §4.1): Ignition → Liftoff → Cruise → Boost →
/// Afterburner → Orbit, then Escape Velocity every 7,000 FP beyond. Pure functions so the
/// ladder is trivially unit-testable and shared by the API and any future clients.
/// </summary>
public static class FuelLevels
{
    /// <summary>Rank name + cumulative FP floor for the named levels, in order.</summary>
    public static readonly IReadOnlyList<(string Rank, long Floor)> Ladder = new (string, long)[]
    {
        ("Ignition", 0),
        ("Liftoff", 500),
        ("Cruise", 1_500),
        ("Boost", 3_500),
        ("Afterburner", 7_000),
        ("Orbit", 12_000),
    };

    /// <summary>FP per level once past the named ladder.</summary>
    public const long PointsPerLevelBeyond = 7_000;

    /// <summary>Rank name for every level past the named ladder.</summary>
    public const string BeyondRank = "Escape Velocity";

    /// <summary>Resolves a lifetime FP total to its level (1-based), rank, floor, and next-level threshold.</summary>
    public static (int Number, string Rank, long Floor, long Next) ForPoints(long points)
    {
        if (points < 0) points = 0;

        for (var i = Ladder.Count - 1; i >= 0; i--)
        {
            if (points < Ladder[i].Floor) continue;

            if (i < Ladder.Count - 1 || points < Ladder[i].Floor + PointsPerLevelBeyond)
            {
                var next = i < Ladder.Count - 1 ? Ladder[i + 1].Floor : Ladder[i].Floor + PointsPerLevelBeyond;
                return (i + 1, Ladder[i].Rank, Ladder[i].Floor, next);
            }

            // Past Orbit: Escape Velocity levels every PointsPerLevelBeyond.
            var beyond = (points - Ladder[^1].Floor) / PointsPerLevelBeyond; // ≥ 1 here
            var floor = Ladder[^1].Floor + beyond * PointsPerLevelBeyond;
            return ((int)(Ladder.Count + beyond), BeyondRank, floor, floor + PointsPerLevelBeyond);
        }

        return (1, Ladder[0].Rank, 0, Ladder[1].Floor);
    }

    /// <summary>The Monday on or before <paramref name="date"/> (weeks run Monday–Sunday).</summary>
    public static DateOnly WeekStartOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
