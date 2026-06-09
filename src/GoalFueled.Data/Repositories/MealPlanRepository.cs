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
        // Plans are personal: visible = the caller's own only (no app-level sharing). The owner filter
        // is a translatable equality predicate; the name search and ordering run in memory because the
        // Cosmos provider can't translate ToLower()/Contains reliably.
        var visible = await _db.MealPlans.Where(p => p.OwnerUserId == userId).ToListAsync(ct);
        return Refine(visible, search);
    }

    public async Task<IReadOnlyList<MealPlan>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every meal plan regardless of owner.
        var all = await _db.MealPlans.ToListAsync(ct);
        return Refine(all, search);
    }

    /// <summary>Applies the optional case-insensitive name search and a stable name ordering in memory.</summary>
    private static IReadOnlyList<MealPlan> Refine(List<MealPlan> plans, string? search)
    {
        IEnumerable<MealPlan> result = plans;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
