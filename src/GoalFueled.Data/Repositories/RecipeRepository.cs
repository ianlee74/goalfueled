using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class RecipeRepository : IRecipeRepository
{
    private readonly GoalFueledDbContext _db;

    public RecipeRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<Recipe>> ListVisibleAsync(
        string userId, string? search = null, CancellationToken ct = default)
    {
        // Visible = app-level recipes + the caller's own (any visibility) + every public recipe.
        // The scope filter is a translatable equality predicate; the name search and ordering are
        // applied in memory because the Cosmos provider can't translate ToLower()/Contains reliably.
        var visible = await _db.Recipes
            .Where(r => r.Scope == OwnershipScope.App ||
                        (r.Scope == OwnershipScope.User && r.OwnerUserId == userId) ||
                        r.Visibility == RecipeVisibility.Public)
            .ToListAsync(ct);

        return Refine(visible, search);
    }

    public async Task<IReadOnlyList<Recipe>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every recipe regardless of owner/scope/visibility.
        var all = await _db.Recipes.ToListAsync(ct);
        return Refine(all, search);
    }

    public async Task<IReadOnlyList<Recipe>> ListPendingReviewAsync(CancellationToken ct = default)
    {
        // Order in memory: the Cosmos provider's ORDER BY on DateTimeOffset is unreliable.
        var pending = await _db.Recipes
            .Where(r => r.Visibility == RecipeVisibility.PendingReview)
            .ToListAsync(ct);
        return pending.OrderBy(r => r.UpdatedAt).ToList();
    }

    public async Task<int> CountPendingReviewByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        // Materialize the (small) owner-scoped set; the Cosmos provider can't translate Count() here.
        var pending = await _db.Recipes
            .Where(r => r.Scope == OwnershipScope.User &&
                        r.OwnerUserId == ownerUserId &&
                        r.Visibility == RecipeVisibility.PendingReview)
            .ToListAsync(ct);
        return pending.Count;
    }

    /// <summary>Applies the optional case-insensitive name search and a stable name ordering in memory.</summary>
    private static IReadOnlyList<Recipe> Refine(List<Recipe> recipes, string? search)
    {
        IEnumerable<Recipe> result = recipes;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<Recipe>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        // Owner-scoped equality is translatable; the (small) set is materialized as-is.
        return await _db.Recipes
            .Where(r => r.Scope == OwnershipScope.User && r.OwnerUserId == ownerUserId)
            .ToListAsync(ct);
    }

    public Task<Recipe?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Recipes.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct = default)
    {
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync(ct);
        return recipe;
    }

    public async Task UpdateAsync(Recipe recipe, CancellationToken ct = default)
    {
        _db.Recipes.Update(recipe);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Recipe recipe, CancellationToken ct = default)
    {
        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync(ct);
    }
}
