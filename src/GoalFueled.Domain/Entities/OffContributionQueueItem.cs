namespace GoalFueled.Domain.Entities;

/// <summary>
/// A pending Open Food Facts contribution. Because the OFF API is metered, newly-added barcoded products
/// aren't pushed inline on save; instead one row per barcode is queued here and drained by a timer in
/// rate-limited batches. The row is deleted once contributed (or once it's clear there's nothing to do —
/// the product is already on OFF, the ingredient was deleted, etc.).
/// </summary>
public class OffContributionQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The product barcode to contribute. At most one pending row exists per barcode.</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>The ingredient whose current data is sent; re-read at send time so edits are picked up.</summary>
    public string IngredientId { get; set; } = string.Empty;

    /// <summary>How many times sending has been attempted and failed. The worker gives up after a cap.</summary>
    public int Attempts { get; set; }

    /// <summary>The last failure reason, for debugging while the row lives.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptAt { get; set; }
}
