using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly GoalFueledDbContext _db;

    public UserRepository(GoalFueledDbContext db) => _db = db;

    public Task<AppUser?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<AppUser?> GetByExternalLoginAsync(string provider, string subject, CancellationToken ct = default)
    {
        var key = AppUser.LoginKey(provider, subject);
        // The Cosmos provider can't translate List<string>.Contains in a predicate, so materialize the
        // (small) user set and match the login key client-side — the same approach as CountAdminsAsync.
        var users = await _db.Users.ToListAsync(ct);
        return users.FirstOrDefault(u => u.LoginKeys.Contains(key));
    }

    public async Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }

    // The Cosmos provider can't translate a bare Any(); a Take(1) read is the portable existence check.
    public async Task<bool> AnyAsync(CancellationToken ct = default) =>
        (await _db.Users.Take(1).ToListAsync(ct)).Count != 0;

    public async Task<IReadOnlyList<AppUser>> ListAllAsync(CancellationToken ct = default)
    {
        // Order in memory: the Cosmos provider's ORDER BY support is limited, and the user set is small.
        var users = await _db.Users.ToListAsync(ct);
        return users.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<int> CountAdminsAsync(CancellationToken ct = default)
    {
        // Roles is an owned collection, so count the admin role in memory after materializing the
        // (small) user set rather than relying on provider-specific translation of List.Contains.
        var users = await _db.Users.ToListAsync(ct);
        return users.Count(u => u.Roles.Contains(AppRole.AppAdmin));
    }
}
