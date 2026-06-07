using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class IngredientRepository : IIngredientRepository
{
    private readonly GoalFueledDbContext _db;

    public IngredientRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<Ingredient>> ListVisibleAsync(
        string userId, string? search = null, CancellationToken ct = default)
    {
        // Visible = app-level ingredients + the caller's own.
        var query = _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.App ||
                        (i.Scope == OwnershipScope.User && i.OwnerUserId == userId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term));
        }

        return await query.OrderBy(i => i.Name).ToListAsync(ct);
    }

    public Task<Ingredient?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Ingredient> AddAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync(ct);
        return ingredient;
    }

    public async Task UpdateAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        _db.Ingredients.Update(ingredient);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        _db.Ingredients.Remove(ingredient);
        await _db.SaveChangesAsync(ct);
    }
}
