using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IWeightEntryRepository
{
    /// <summary>The user's weight entries, most recent day first.</summary>
    Task<IReadOnlyList<WeightEntry>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>The user's entry for a specific day, or null if none exists.</summary>
    Task<WeightEntry?> GetForUserAndDateAsync(string userId, DateOnly date, CancellationToken ct = default);

    Task<WeightEntry> AddAsync(WeightEntry entry, CancellationToken ct = default);

    Task UpdateAsync(WeightEntry entry, CancellationToken ct = default);

    Task DeleteAsync(WeightEntry entry, CancellationToken ct = default);
}
