using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IConsumptionEntryRepository
{
    /// <summary>The user's consumption entries for a specific day (most recently logged first).</summary>
    Task<IReadOnlyList<ConsumptionEntry>> ListForUserAndDateAsync(string userId, DateOnly date, CancellationToken ct = default);

    /// <summary>A single entry by id, or null if it does not exist.</summary>
    Task<ConsumptionEntry?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ConsumptionEntry> AddAsync(ConsumptionEntry entry, CancellationToken ct = default);

    Task DeleteAsync(ConsumptionEntry entry, CancellationToken ct = default);
}
