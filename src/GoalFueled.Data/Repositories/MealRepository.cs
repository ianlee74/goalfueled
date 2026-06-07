using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class MealRepository : IMealRepository
{
    private readonly GoalFueledDbContext _db;

    public MealRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<Meal>> ListVisibleAsync(
        string userId, string? search = null, CancellationToken ct = default)
    {
        // Visible = app-level meals + the caller's own.
        var query = _db.Meals
            .Where(m => m.Scope == OwnershipScope.App ||
                        (m.Scope == OwnershipScope.User && m.OwnerUserId == userId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(term));
        }

        return await query.OrderBy(m => m.Name).ToListAsync(ct);
    }

    public Task<Meal?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Meals.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<Meal> AddAsync(Meal meal, CancellationToken ct = default)
    {
        _db.Meals.Add(meal);
        await _db.SaveChangesAsync(ct);
        return meal;
    }

    public async Task UpdateAsync(Meal meal, CancellationToken ct = default)
    {
        _db.Meals.Update(meal);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Meal meal, CancellationToken ct = default)
    {
        _db.Meals.Remove(meal);
        await _db.SaveChangesAsync(ct);
    }
}
