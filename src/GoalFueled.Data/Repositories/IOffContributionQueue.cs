using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

/// <summary>
/// Persists pending Open Food Facts contributions so they can be sent later, in rate-limited batches, by
/// a timer. At most one pending row exists per barcode.
/// </summary>
public interface IOffContributionQueue
{
    /// <summary>
    /// Enqueues a barcode for contribution, or refreshes the existing pending row for that barcode to point
    /// at the latest ingredient (and reset its retry state). Cheap: a single lookup + write, no OFF call.
    /// </summary>
    Task UpsertAsync(string barcode, string ingredientId, CancellationToken ct = default);

    /// <summary>Returns up to <paramref name="max"/> pending items, oldest first.</summary>
    Task<IReadOnlyList<OffContributionQueueItem>> ListPendingAsync(int max, CancellationToken ct = default);

    Task UpdateAsync(OffContributionQueueItem item, CancellationToken ct = default);

    Task RemoveAsync(OffContributionQueueItem item, CancellationToken ct = default);
}
