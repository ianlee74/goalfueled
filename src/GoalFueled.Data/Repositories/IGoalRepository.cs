using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IGoalRepository
{
    /// <summary>The user's goals, most recently created first.</summary>
    Task<IReadOnlyList<Goal>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>A single goal by id, or null if it does not exist.</summary>
    Task<Goal?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Goal> AddAsync(Goal goal, CancellationToken ct = default);

    Task UpdateAsync(Goal goal, CancellationToken ct = default);

    Task DeleteAsync(Goal goal, CancellationToken ct = default);
}
