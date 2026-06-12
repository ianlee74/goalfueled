using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class UserStatsRepository : IUserStatsRepository
{
    private readonly GoalFueledDbContext _db;

    public UserStatsRepository(GoalFueledDbContext db) => _db = db;

    public async Task<UserStats?> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        // One document per user; materialize then pick so the lookup doesn't depend on the
        // provider's translation quirks (mirrors the other repositories).
        var stats = await _db.UserStats
            .Where(s => s.OwnerUserId == userId)
            .ToListAsync(ct);
        return stats.FirstOrDefault();
    }

    public async Task<UserStats> AddAsync(UserStats stats, CancellationToken ct = default)
    {
        _db.UserStats.Add(stats);
        await _db.SaveChangesAsync(ct);
        return stats;
    }

    public async Task UpdateAsync(UserStats stats, CancellationToken ct = default)
    {
        _db.UserStats.Update(stats);
        await _db.SaveChangesAsync(ct);
    }
}
