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

    public Task<AppUser?> GetByExternalLoginAsync(string provider, string subject, CancellationToken ct = default)
    {
        var key = AppUser.LoginKey(provider, subject);
        return _db.Users.FirstOrDefaultAsync(u => u.LoginKeys.Contains(key), ct);
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

    public Task<bool> AnyAsync(CancellationToken ct = default) => _db.Users.AnyAsync(ct);

    public async Task<IReadOnlyList<AppUser>> ListAllAsync(CancellationToken ct = default) =>
        await _db.Users.OrderBy(u => u.DisplayName).ToListAsync(ct);

    public async Task<int> CountAdminsAsync(CancellationToken ct = default)
    {
        // Roles is an owned collection, so count the admin role in memory after materializing the
        // (small) user set rather than relying on provider-specific translation of List.Contains.
        var users = await _db.Users.ToListAsync(ct);
        return users.Count(u => u.Roles.Contains(AppRole.AppAdmin));
    }
}
