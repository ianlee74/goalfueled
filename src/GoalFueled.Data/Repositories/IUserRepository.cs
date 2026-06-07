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
}
