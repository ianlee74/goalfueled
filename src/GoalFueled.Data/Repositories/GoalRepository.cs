using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class GoalRepository : IGoalRepository
{
    private readonly GoalFueledDbContext _db;

    public GoalRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<Goal>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        // A user's goal set is small; materialize then order in memory so ordering does not depend on
        // the provider's translation of DateTimeOffset comparisons (Cosmos vs in-memory).
        var goals = await _db.Goals
            .Where(g => g.OwnerUserId == userId)
            .ToListAsync(ct);
        return goals.OrderByDescending(g => g.CreatedAt).ToList();
    }

    public Task<Goal?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Goals.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<Goal> AddAsync(Goal goal, CancellationToken ct = default)
    {
        _db.Goals.Add(goal);
        await _db.SaveChangesAsync(ct);
        return goal;
    }

    public async Task UpdateAsync(Goal goal, CancellationToken ct = default)
    {
        _db.Goals.Update(goal);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Goal goal, CancellationToken ct = default)
    {
        _db.Goals.Remove(goal);
        await _db.SaveChangesAsync(ct);
    }
}
