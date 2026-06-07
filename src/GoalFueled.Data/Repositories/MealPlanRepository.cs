using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class MealPlanRepository : IMealPlanRepository
{
    private readonly GoalFueledDbContext _db;

    public MealPlanRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<MealPlan>> ListVisibleAsync(
        string userId, string? search = null, CancellationToken ct = default)
    {
        // Plans are personal: visible = the caller's own only (no app-level sharing).
        var query = _db.MealPlans.Where(p => p.OwnerUserId == userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public Task<MealPlan?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.MealPlans.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<MealPlan> AddAsync(MealPlan plan, CancellationToken ct = default)
    {
        _db.MealPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return plan;
    }

    public async Task UpdateAsync(MealPlan plan, CancellationToken ct = default)
    {
        _db.MealPlans.Update(plan);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(MealPlan plan, CancellationToken ct = default)
    {
        _db.MealPlans.Remove(plan);
        await _db.SaveChangesAsync(ct);
    }
}
