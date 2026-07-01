using System.Globalization;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>What happened when a single product was processed for contribution.</summary>
public enum OffContributionOutcome
{
    /// <summary>No write account is configured; contribution is off.</summary>
    Disabled,
    /// <summary>The ingredient has no barcode; nothing to contribute.</summary>
    NoBarcode,
    /// <summary>OFF already has this product; skipped (this is also what stops OFF-sourced items being re-pushed).</summary>
    AlreadyPresent,
    /// <summary>The ingredient produced no usable nutriments to send.</summary>
    NoData,
    /// <summary>The product was written to OFF.</summary>
    Contributed,
    /// <summary>The write was attempted but not accepted; safe to retry later.</summary>
    Failed,
}

/// <summary>
/// Contributes newly-added products back to Open Food Facts. Because the OFF API is metered, saves don't
/// push inline — <see cref="EnqueueIfEligibleAsync"/> records the barcode and a timer later drains the
/// queue via <see cref="ProcessQueueAsync"/>. A product is only sent when OFF doesn't already have it, so
/// any product the user adds that OFF is missing gets pushed, and OFF-sourced items are never re-pushed.
/// Best-effort throughout: failures are swallowed/retried and never surface to the user.
/// </summary>
public sealed class OffContributionService
{
    /// <summary>How many times to retry a failing contribution before giving up and dropping it.</summary>
    public const int MaxAttempts = 5;

    private readonly IOffClient _client;
    private readonly IOffContributionQueue _queue;
    private readonly IIngredientRepository _ingredients;
    private readonly ILogger<OffContributionService> _logger;

    public OffContributionService(
        IOffClient client, IOffContributionQueue queue, IIngredientRepository ingredients,
        ILogger<OffContributionService> logger)
    {
        _client = client;
        _queue = queue;
        _ingredients = ingredients;
        _logger = logger;
    }

    /// <summary>
    /// Queues a newly-added barcoded product for later contribution. No-op (and no OFF call) when there's
    /// no write account or no barcode. Cheap enough to await on the save path.
    /// </summary>
    public async Task EnqueueIfEligibleAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        try
        {
            if (!_client.CanContribute) return;
            if (string.IsNullOrWhiteSpace(ingredient.Barcode)) return;
            await _queue.UpsertAsync(ingredient.Barcode, ingredient.Id, ct);
        }
        catch (Exception ex)
        {
            // Queuing must never fail a save.
            _logger.LogWarning(ex, "Failed to queue OFF contribution for '{Barcode}'.", ingredient.Barcode);
        }
    }

    /// <summary>
    /// Drains up to <paramref name="batchSize"/> queued contributions, spacing OFF calls out by
    /// <paramref name="delay"/> to respect the metered API. Sent/obsolete items are removed; transient
    /// failures are retried on a later run until <see cref="MaxAttempts"/>. Returns the number contributed.
    /// </summary>
    public async Task<int> ProcessQueueAsync(int batchSize, TimeSpan delay, CancellationToken ct = default)
    {
        if (!_client.CanContribute) return 0;

        var pending = await _queue.ListPendingAsync(batchSize, ct);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contributed = 0;
        var madeOffCall = false;

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Collapse duplicate barcodes within this batch (belt-and-suspenders — Upsert keeps one row).
            if (!seenBarcodes.Add(item.Barcode)) { await _queue.RemoveAsync(item, ct); continue; }

            // Re-read the ingredient so we send its current data. Drop the row if it's gone, its barcode
            // changed, or it is no longer public — only vetted, public products are contributed, and this
            // is the authoritative gate even if visibility changed after the item was queued.
            var ingredient = await _ingredients.GetByIdAsync(item.IngredientId, ct);
            if (ingredient is null ||
                ingredient.Barcode != item.Barcode ||
                ingredient.EffectiveVisibility != IngredientVisibility.Public)
            {
                await _queue.RemoveAsync(item, ct);
                continue;
            }

            // Space out network calls to stay under OFF's rate limits.
            if (madeOffCall) await Task.Delay(delay, ct);
            madeOffCall = true;

            var outcome = await ContributeIfNewAsync(ingredient, ct);
            switch (outcome)
            {
                case OffContributionOutcome.Contributed:
                    contributed++;
                    await _queue.RemoveAsync(item, ct);
                    break;

                // Nothing more to do for this barcode — drop it.
                case OffContributionOutcome.AlreadyPresent:
                case OffContributionOutcome.NoData:
                case OffContributionOutcome.NoBarcode:
                    await _queue.RemoveAsync(item, ct);
                    break;

                case OffContributionOutcome.Failed:
                    item.Attempts++;
                    item.LastError = "Write not accepted.";
                    item.LastAttemptAt = DateTimeOffset.UtcNow;
                    if (item.Attempts >= MaxAttempts)
                    {
                        _logger.LogWarning(
                            "Giving up on OFF contribution for '{Barcode}' after {Attempts} attempts.",
                            item.Barcode, item.Attempts);
                        await _queue.RemoveAsync(item, ct);
                    }
                    else
                    {
                        await _queue.UpdateAsync(item, ct);
                    }
                    break;

                case OffContributionOutcome.Disabled:
                    // Shouldn't happen (guarded above); leave the item for when an account is configured.
                    return contributed;
            }
        }

        return contributed;
    }

    /// <summary>
    /// Contributes one product to OFF when it has a barcode OFF doesn't already know. Never throws.
    /// </summary>
    public async Task<OffContributionOutcome> ContributeIfNewAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        try
        {
            if (!_client.CanContribute) return OffContributionOutcome.Disabled;
            if (string.IsNullOrWhiteSpace(ingredient.Barcode)) return OffContributionOutcome.NoBarcode;

            // Skip products OFF already has — this is what prevents re-pushing OFF-sourced items.
            var existing = await _client.GetProductAsync(ingredient.Barcode, ct);
            if (existing is not null) return OffContributionOutcome.AlreadyPresent;

            var fields = BuildFields(ingredient);
            if (fields.Count == 0) return OffContributionOutcome.NoData;

            var ok = await _client.ContributeAsync(ingredient.Barcode, fields, ct);
            _logger.LogInformation(
                "OFF contribution for '{Barcode}' ({Name}) {Outcome}.",
                ingredient.Barcode, ingredient.Name, ok ? "succeeded" : "was not accepted");
            return ok ? OffContributionOutcome.Contributed : OffContributionOutcome.Failed;
        }
        catch (Exception ex)
        {
            // Best-effort: contribution must never surface as a failure.
            _logger.LogWarning(ex, "OFF contribution for '{Barcode}' threw and was ignored.", ingredient.Barcode);
            return OffContributionOutcome.Failed;
        }
    }

    /// <summary>
    /// Builds the product/nutriment form fields for a write. Mass-based servings are expressed per 100 g
    /// (<c>nutrition_data_per=100g</c>); volume/count servings can't be put on a per-100-g basis, so they
    /// are sent per serving with a <c>serving_size</c> label. Nutriments are converted to OFF's canonical
    /// units (kcal for energy, grams otherwise).
    /// </summary>
    public static List<KeyValuePair<string, string>> BuildFields(Ingredient ingredient)
    {
        var fields = new List<KeyValuePair<string, string>>();

        if (!string.IsNullOrWhiteSpace(ingredient.Name))
            fields.Add(new("product_name", ingredient.Name.Trim()));

        var info = Units.Get(ingredient.Unit);

        // Scale factor from per-serving to the basis we report, and the basis label OFF expects.
        double scale;
        if (info.Dimension == UnitDimension.Mass && ingredient.ServingSize > 0)
        {
            var servingGrams = ingredient.ServingSize * info.ToBaseFactor;
            if (servingGrams <= 0) return fields;
            scale = 100d / servingGrams; // per-serving → per-100 g
            fields.Add(new("nutrition_data_per", "100g"));
        }
        else
        {
            // Non-mass (or zero) serving: report the values as-is, per the labelled serving.
            scale = 1d;
            fields.Add(new("nutrition_data_per", "serving"));
            fields.Add(new("serving_size",
                $"{ingredient.ServingSize.ToString("0.######", CultureInfo.InvariantCulture)} {info.Abbreviation}"));
        }

        foreach (var amount in ingredient.NutritionPerServing)
        {
            var offKey = OffNutrimentMap.ToOffBaseKey(amount.NutrientId);
            if (offKey is null) continue;
            var def = NutrientCatalog.Find(amount.NutrientId);
            if (def is null) continue;

            var canonical = OffNutrimentMap.ToOffCanonical(amount.Value * scale, def);
            if (canonical is null || canonical < 0) continue;

            fields.Add(new($"nutriment_{offKey}",
                canonical.Value.ToString("0.######", CultureInfo.InvariantCulture)));
            fields.Add(new($"nutriment_{offKey}_unit", def.Unit == NutrientUnit.Kcal ? "kcal" : "g"));
        }

        // If we produced only the basis marker(s) and no nutriments, there's nothing worth contributing.
        return fields.Any(f => f.Key.StartsWith("nutriment_", StringComparison.Ordinal))
            ? fields
            : new List<KeyValuePair<string, string>>();
    }
}
