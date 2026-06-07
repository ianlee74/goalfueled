using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IIngredientRepository
{
    /// <summary>
    /// Returns ingredients visible to a user: all app-level ingredients plus the user's own.
    /// Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<Ingredient>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default);

    Task<Ingredient?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Ingredient> AddAsync(Ingredient ingredient, CancellationToken ct = default);

    Task UpdateAsync(Ingredient ingredient, CancellationToken ct = default);

    Task DeleteAsync(Ingredient ingredient, CancellationToken ct = default);
}
