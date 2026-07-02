using GoalFueled.Domain;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Verifies, server-side, that an ingredient's data matches the Open Food Facts record for its barcode
/// verbatim. Data that matches is already public on OFF, so such ingredients skip the review workflow
/// and become app-wide immediately. The check is done on the server (re-fetching OFF) rather than
/// trusting a client-supplied flag, so it cannot be used to smuggle unreviewed data past review.
/// </summary>
public sealed class OffVerificationService
{
    /// <summary>
    /// Relative tolerance for comparing nutrient values. Values round-trip client-side as IEEE-754
    /// doubles, so a verbatim save is near-exact; this only absorbs float noise, not edits.
    /// </summary>
    private const double RelativeTolerance = 1e-6;

    private readonly IOffClient _client;

    public OffVerificationService(IOffClient client) => _client = client;

    /// <summary>
    /// True when the ingredient's serving basis is OFF's (100 g) and its nutrition matches the OFF
    /// record for its barcode exactly (same nutrient set, same values). Name/description/tags/image are
    /// deliberately NOT compared — they're presentation, not nutrition data, and users may tidy them
    /// without forfeiting the verbatim-data promotion. Returns false on any miss, empty/implausible OFF
    /// nutrition, or an unreachable OFF (never throws) — callers then fall back to the normal review flow.
    /// </summary>
    public async Task<bool> MatchesOffAsync(Ingredient ingredient, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ingredient.Barcode)) return false;
        // OFF prefill is always per 100 g; any other basis means the user changed the data.
        if (ingredient.Unit != UnitOfMeasure.Gram || Math.Abs(ingredient.ServingSize - 100) > 0.001) return false;
        if (ingredient.NutritionPerServing.Count == 0) return false;

        var product = await _client.GetProductAsync(ingredient.Barcode, ct);
        if (product?.Nutriments is null) return false;

        var offAmounts = OffNutrimentMap.MapPer100g(product.Nutriments);
        if (offAmounts.Count == 0) return false; // nothing usable on OFF (or record rejected as implausible)
        if (offAmounts.Count != ingredient.NutritionPerServing.Count) return false;

        var byId = ingredient.NutritionPerServing
            .ToDictionary(a => a.NutrientId, a => a.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var off in offAmounts)
        {
            if (!byId.TryGetValue(off.NutrientId, out var value)) return false;
            var scale = Math.Max(Math.Abs(off.Value), 1);
            if (Math.Abs(value - off.Value) > scale * RelativeTolerance) return false;
        }

        return true;
    }
}
