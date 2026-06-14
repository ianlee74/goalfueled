using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IMealPlanRepository
{
    /// <summary>
    /// Returns meal plans visible to a user. Plans are personal — there is no app-level sharing —
    /// so this is exactly the user's own plans. Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<MealPlan>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Returns every meal plan regardless of owner. Admin-only ("view all"); callers must gate on
    /// <c>IsActingAsAdmin</c>. Optionally filters by a case-insensitive name search.
    /// </summary>
    Task<IReadOnlyList<MealPlan>> ListAllAsync(string? search = null, CancellationToken ct = default);

    /// <summary>Returns every meal plan owned by the given user. Used when cascade-deleting a user.</summary>
    Task<IReadOnlyList<MealPlan>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    Task<MealPlan?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<MealPlan> AddAsync(MealPlan plan, CancellationToken ct = default);

    Task UpdateAsync(MealPlan plan, CancellationToken ct = default);

    Task DeleteAsync(MealPlan plan, CancellationToken ct = default);
}
