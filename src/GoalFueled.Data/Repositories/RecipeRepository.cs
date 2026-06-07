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
        var query = _db.Recipes
            .Where(r => r.Scope == OwnershipScope.App ||
                        (r.Scope == OwnershipScope.User && r.OwnerUserId == userId) ||
                        r.Visibility == RecipeVisibility.Public);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }

        return await query.OrderBy(r => r.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Recipe>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every recipe regardless of owner/scope/visibility.
        var query = _db.Recipes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }

        return await query.OrderBy(r => r.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Recipe>> ListPendingReviewAsync(CancellationToken ct = default)
    {
        return await _db.Recipes
            .Where(r => r.Visibility == RecipeVisibility.PendingReview)
            .OrderBy(r => r.UpdatedAt)
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
