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
        // Visible = app-level meals + the caller's own. The scope filter is a translatable equality
        // predicate; the name search and ordering run in memory because the Cosmos provider can't
        // translate ToLower()/Contains reliably.
        var visible = await _db.Meals
            .Where(m => m.Scope == OwnershipScope.App ||
                        (m.Scope == OwnershipScope.User && m.OwnerUserId == userId))
            .ToListAsync(ct);

        return Refine(visible, search);
    }

    public async Task<IReadOnlyList<Meal>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every meal regardless of owner/scope.
        var all = await _db.Meals.ToListAsync(ct);
        return Refine(all, search);
    }

    /// <summary>Applies the optional case-insensitive name search and a stable name ordering in memory.</summary>
    private static IReadOnlyList<Meal> Refine(List<Meal> meals, string? search)
    {
        IEnumerable<Meal> result = meals;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(m => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
