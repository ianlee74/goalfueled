using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IUserStatsRepository
{
    /// <summary>The user's stats document, or null when none exists yet (created on first award).</summary>
    Task<UserStats?> GetForUserAsync(string userId, CancellationToken ct = default);

    Task<UserStats> AddAsync(UserStats stats, CancellationToken ct = default);

    Task UpdateAsync(UserStats stats, CancellationToken ct = default);

    Task DeleteAsync(UserStats stats, CancellationToken ct = default);
}
