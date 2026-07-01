using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IIngredientRepository
{
    /// <summary>
    /// Returns ingredients visible to a user: all app-level ingredients, the user's own (any
    /// visibility), and every public ingredient. Optionally filters by a case- and accent-insensitive
    /// search across name, aliases, tags and non-English names.
    /// </summary>
    Task<IReadOnlyList<Ingredient>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Returns every ingredient regardless of owner. Admin-only ("view all"); callers must gate on
    /// <c>IsActingAsAdmin</c>. Optionally filters by the same search as <see cref="ListVisibleAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Ingredient>> ListAllAsync(string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Returns all ingredients currently awaiting admin review (the public-ingredient review queue).
    /// </summary>
    Task<IReadOnlyList<Ingredient>> ListPendingReviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Counts the ingredients owned by a user that are currently awaiting review. Used to cap how many
    /// submissions one user can have in flight at once.
    /// </summary>
    Task<int> CountPendingReviewByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns every ingredient owned by the given user (user-scoped only). Used when cascade-deleting
    /// a user: their private ingredients are deleted and their approved/public ones are re-homed to the app.
    /// </summary>
    Task<IReadOnlyList<Ingredient>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    Task<Ingredient?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns the ingredient visible to the user that carries the given barcode, or null. Used to
    /// short-circuit a re-scan to the existing library item instead of creating a duplicate. Visibility
    /// mirrors <see cref="ListVisibleAsync"/>: app-level, the user's own, and any public ingredient.
    /// </summary>
    Task<Ingredient?> GetByBarcodeVisibleAsync(string userId, string barcode, CancellationToken ct = default);

    Task<Ingredient> AddAsync(Ingredient ingredient, CancellationToken ct = default);

    Task UpdateAsync(Ingredient ingredient, CancellationToken ct = default);

    Task DeleteAsync(Ingredient ingredient, CancellationToken ct = default);
}
