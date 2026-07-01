using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class OffContributionQueue : IOffContributionQueue
{
    private readonly GoalFueledDbContext _db;

    public OffContributionQueue(GoalFueledDbContext db) => _db = db;

    public async Task UpsertAsync(string barcode, string ingredientId, CancellationToken ct = default)
    {
        // Barcode equality is a translatable predicate; a barcode is near-unique so the set is tiny.
        var existing = await _db.OffContributionQueue
            .Where(q => q.Barcode == barcode)
            .ToListAsync(ct);

        if (existing.FirstOrDefault() is { } item)
        {
            // Point at the latest ingredient and give it a fresh set of retries.
            item.IngredientId = ingredientId;
            item.Attempts = 0;
            item.LastError = null;
            item.LastAttemptAt = null;
            _db.OffContributionQueue.Update(item);

            // Collapse any accidental duplicate rows for the same barcode down to one.
            foreach (var dup in existing.Skip(1))
                _db.OffContributionQueue.Remove(dup);
        }
        else
        {
            _db.OffContributionQueue.Add(new OffContributionQueueItem
            {
                Barcode = barcode,
                IngredientId = ingredientId,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OffContributionQueueItem>> ListPendingAsync(int max, CancellationToken ct = default)
    {
        // Order in memory: the Cosmos provider's ORDER BY on DateTimeOffset is unreliable (see the
        // ingredient repository's review-queue note).
        var all = await _db.OffContributionQueue.ToListAsync(ct);
        return all.OrderBy(q => q.CreatedAt).Take(max).ToList();
    }

    public async Task UpdateAsync(OffContributionQueueItem item, CancellationToken ct = default)
    {
        _db.OffContributionQueue.Update(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(OffContributionQueueItem item, CancellationToken ct = default)
    {
        _db.OffContributionQueue.Remove(item);
        await _db.SaveChangesAsync(ct);
    }
}
