using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IBurnActivityRepository
{
    /// <summary>The user's burn activities for a specific day (most recently logged first).</summary>
    Task<IReadOnlyList<BurnActivity>> ListForUserAndDateAsync(string userId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The user's burn activities whose date falls within the inclusive range <paramref name="from"/>..
    /// <paramref name="to"/> (most recently logged first). Used to roll up burned calories over a period.
    /// </summary>
    Task<IReadOnlyList<BurnActivity>> ListForUserInRangeAsync(
        string userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>The user's entire burn history (most recently logged first); drives the name suggestions.</summary>
    Task<IReadOnlyList<BurnActivity>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>A single activity by id, or null if it does not exist.</summary>
    Task<BurnActivity?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<BurnActivity> AddAsync(BurnActivity activity, CancellationToken ct = default);

    Task DeleteAsync(BurnActivity activity, CancellationToken ct = default);
}
