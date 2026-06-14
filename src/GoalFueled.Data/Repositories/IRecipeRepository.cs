using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IRecipeRepository
{
    /// <summary>
    /// Returns recipes visible to a user: all app-level recipes, the user's own (any visibility),
    /// and every public recipe. Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<Recipe>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Returns every recipe regardless of owner/visibility. Admin-only ("view all"); callers must gate
    /// on <c>IsActingAsAdmin</c>. Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<Recipe>> ListAllAsync(string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Returns all recipes currently awaiting admin review (the public-recipe review queue).
    /// </summary>
    Task<IReadOnlyList<Recipe>> ListPendingReviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Counts the recipes owned by a user that are currently awaiting review. Used to cap how many
    /// submissions one user can have in flight at once.
    /// </summary>
    Task<int> CountPendingReviewByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns every recipe owned by the given user (user-scoped only). Used when cascade-deleting a
    /// user: their private recipes are deleted and their approved/public ones are re-homed to the app.
    /// </summary>
    Task<IReadOnlyList<Recipe>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    Task<Recipe?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct = default);

    Task UpdateAsync(Recipe recipe, CancellationToken ct = default);

    Task DeleteAsync(Recipe recipe, CancellationToken ct = default);
}
