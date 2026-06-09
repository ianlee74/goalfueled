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
        // Visible = app-level ingredients + the caller's own. The scope filter is a translatable
        // equality predicate; the name search and ordering run in memory because the Cosmos provider
        // can't translate ToLower()/Contains reliably.
        var visible = await _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.App ||
                        (i.Scope == OwnershipScope.User && i.OwnerUserId == userId))
            .ToListAsync(ct);

        return Refine(visible, search);
    }

    public async Task<IReadOnlyList<Ingredient>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every ingredient regardless of owner/scope.
        var all = await _db.Ingredients.ToListAsync(ct);
        return Refine(all, search);
    }

    /// <summary>Applies the optional case-insensitive name search and a stable name ordering in memory.</summary>
    private static IReadOnlyList<Ingredient> Refine(List<Ingredient> ingredients, string? search)
    {
        IEnumerable<Ingredient> result = ingredients;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
