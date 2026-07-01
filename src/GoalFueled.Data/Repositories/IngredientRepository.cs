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
        // Visible = app-level ingredients + the caller's own (any visibility) + every public one.
        // The scope filter is a translatable equality predicate; the name search and ordering run in
        // memory because the Cosmos provider can't translate ToLower()/Contains reliably.
        var visible = await _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.App ||
                        (i.Scope == OwnershipScope.User && i.OwnerUserId == userId) ||
                        i.Visibility == IngredientVisibility.Public)
            .ToListAsync(ct);

        return Refine(visible, search);
    }

    public async Task<IReadOnlyList<Ingredient>> ListAllAsync(string? search = null, CancellationToken ct = default)
    {
        // Admin "view all": every ingredient regardless of owner/scope/visibility.
        var all = await _db.Ingredients.ToListAsync(ct);
        return Refine(all, search);
    }

    public async Task<IReadOnlyList<Ingredient>> ListPendingReviewAsync(CancellationToken ct = default)
    {
        // Order in memory: the Cosmos provider's ORDER BY on DateTimeOffset is unreliable.
        var pending = await _db.Ingredients
            .Where(i => i.Visibility == IngredientVisibility.PendingReview)
            .ToListAsync(ct);
        return pending.OrderBy(i => i.UpdatedAt).ToList();
    }

    public async Task<int> CountPendingReviewByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        // Materialize the (small) owner-scoped set; the Cosmos provider can't translate Count() here.
        var pending = await _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.User &&
                        i.OwnerUserId == ownerUserId &&
                        i.Visibility == IngredientVisibility.PendingReview)
            .ToListAsync(ct);
        return pending.Count;
    }

    /// <summary>
    /// Applies the optional search and a stable name ordering in memory. The search is
    /// case- and accent-insensitive and matches name, aliases, tags and non-English names
    /// (see <see cref="SearchText"/>).
    /// </summary>
    private static IReadOnlyList<Ingredient> Refine(List<Ingredient> ingredients, string? search)
    {
        IEnumerable<Ingredient> result = ingredients;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchText.Fold(search.Trim());
            result = result.Where(i => SearchText.MatchesIngredient(i, term));
        }
        return result.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<Ingredient>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        // Owner-scoped equality is translatable; the (small) set is materialized as-is.
        return await _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.User && i.OwnerUserId == ownerUserId)
            .ToListAsync(ct);
    }

    public Task<Ingredient?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Ingredient?> GetByBarcodeVisibleAsync(
        string userId, string barcode, CancellationToken ct = default)
    {
        // Equality on Barcode is translatable; scope to what the user may see, then prefer their own
        // copy over a public/app one. Materialize the (tiny) match set — barcodes are near-unique.
        var matches = await _db.Ingredients
            .Where(i => i.Barcode == barcode &&
                        (i.Scope == OwnershipScope.App ||
                         (i.Scope == OwnershipScope.User && i.OwnerUserId == userId) ||
                         i.Visibility == IngredientVisibility.Public))
            .ToListAsync(ct);

        return matches.FirstOrDefault(i => i.Scope == OwnershipScope.User && i.OwnerUserId == userId)
            ?? matches.FirstOrDefault();
    }

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
