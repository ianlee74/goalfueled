using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IMealRepository
{
    /// <summary>
    /// Returns meals visible to a user: all app-level meals plus the user's own.
    /// Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<Meal>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default);

    Task<Meal?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Meal> AddAsync(Meal meal, CancellationToken ct = default);

    Task UpdateAsync(Meal meal, CancellationToken ct = default);

    Task DeleteAsync(Meal meal, CancellationToken ct = default);
}
