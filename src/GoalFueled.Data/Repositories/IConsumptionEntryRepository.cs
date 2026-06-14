using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IConsumptionEntryRepository
{
    /// <summary>The user's consumption entries for a specific day (most recently logged first).</summary>
    Task<IReadOnlyList<ConsumptionEntry>> ListForUserAndDateAsync(string userId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The user's consumption entries whose date falls within the inclusive range <paramref name="from"/>..
    /// <paramref name="to"/> (most recently logged first). Used to roll up actual intake over a goal period.
    /// </summary>
    Task<IReadOnlyList<ConsumptionEntry>> ListForUserInRangeAsync(
        string userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>All of the user's consumption entries (used when cascade-deleting a user).</summary>
    Task<IReadOnlyList<ConsumptionEntry>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>A single entry by id, or null if it does not exist.</summary>
    Task<ConsumptionEntry?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ConsumptionEntry> AddAsync(ConsumptionEntry entry, CancellationToken ct = default);

    Task UpdateAsync(ConsumptionEntry entry, CancellationToken ct = default);

    Task DeleteAsync(ConsumptionEntry entry, CancellationToken ct = default);
}
