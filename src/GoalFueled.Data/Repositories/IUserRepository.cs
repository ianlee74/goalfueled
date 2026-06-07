using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IUserRepository
{
    Task<AppUser?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Finds a user by a linked external provider login.</summary>
    Task<AppUser?> GetByExternalLoginAsync(string provider, string subject, CancellationToken ct = default);

    Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default);

    Task UpdateAsync(AppUser user, CancellationToken ct = default);

    /// <summary>True if there are no users yet; used to grant the first user the App Admin role.</summary>
    Task<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>All users, ordered by display name. Admin-only — used by the user-management screen.</summary>
    Task<IReadOnlyList<AppUser>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Count of users holding the App Admin role; used to prevent demoting the last admin.</summary>
    Task<int> CountAdminsAsync(CancellationToken ct = default);
}
