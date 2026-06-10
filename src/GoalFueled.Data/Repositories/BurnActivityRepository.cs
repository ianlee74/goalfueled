using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class BurnActivityRepository : IBurnActivityRepository
{
    private readonly GoalFueledDbContext _db;

    public BurnActivityRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<BurnActivity>> ListForUserAndDateAsync(
        string userId, DateOnly date, CancellationToken ct = default)
    {
        // A user's activities are few; materialize then filter/order by date in memory so this does not
        // depend on the provider's translation of DateOnly comparisons (Cosmos vs in-memory).
        var activities = await _db.BurnActivities
            .Where(a => a.OwnerUserId == userId)
            .ToListAsync(ct);
        return activities
            .Where(a => a.Date == date)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<BurnActivity>> ListForUserInRangeAsync(
        string userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // As above: materialize the user's (few) activities then filter the date range in memory so this
        // does not depend on the provider's translation of DateOnly comparisons (Cosmos vs in-memory).
        var activities = await _db.BurnActivities
            .Where(a => a.OwnerUserId == userId)
            .ToListAsync(ct);
        return activities
            .Where(a => a.Date >= from && a.Date <= to)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<BurnActivity>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        var activities = await _db.BurnActivities
            .Where(a => a.OwnerUserId == userId)
            .ToListAsync(ct);
        return activities.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public Task<BurnActivity?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.BurnActivities.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<BurnActivity> AddAsync(BurnActivity activity, CancellationToken ct = default)
    {
        _db.BurnActivities.Add(activity);
        await _db.SaveChangesAsync(ct);
        return activity;
    }

    public async Task DeleteAsync(BurnActivity activity, CancellationToken ct = default)
    {
        _db.BurnActivities.Remove(activity);
        await _db.SaveChangesAsync(ct);
    }
}
