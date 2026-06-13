using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Looks up nutrition for a free-text ingredient name and maps the external source's data onto this
/// app's nutrient catalog. The external HTTP call is abstracted behind <see cref="IFdcNutritionClient"/>
/// so this mapping/parsing logic is unit-testable without any network access.
/// </summary>
/// <remarks>
/// Two sources are tried, USDA first and the app's own ingredient catalog as an offline fallback:
/// <list type="number">
/// <item>USDA FoodData Central (live HTTP) — rich data, but the public <c>DEMO_KEY</c> is limited to
/// ~10 requests/hour per IP, so it is frequently rate-limited (HTTP 429). When that happens the
/// client returns <c>null</c> and we fall through to the catalog so the button still does something
/// useful.</item>
/// <item>The app's own ingredient catalog — when a visible ingredient's name/alias matches the query,
/// its per-serving nutrition is rescaled to per-100 g and returned. This needs no network and works
/// for the seeded catalog (banana, chicken, rice, …).</item>
/// </list>
/// </remarks>
public sealed class NutritionLookupService
{
    /// <summary>The amount of food the returned values describe (USDA reports per 100 g).</summary>
    public const double BasisGrams = 100d;

    public const string SourceName = "USDA FoodData Central";

    /// <summary>Source label used when the offline fallback fills values from the app's own catalog.</summary>
    public const string CatalogSourceName = "your ingredient catalog";

    private readonly IFdcNutritionClient _client;
    private readonly IIngredientRepository? _ingredients;

    /// <summary>
    /// Production constructor. The repository powers the offline catalog fallback used when the live
    /// USDA lookup is unavailable or rate-limited.
    /// </summary>
    public NutritionLookupService(IFdcNutritionClient client, IIngredientRepository ingredients)
    {
        _client = client;
        _ingredients = ingredients;
    }

    /// <summary>
    /// Test/standalone constructor for exercising the USDA mapping path without a catalog. The offline
    /// fallback is simply skipped when no repository is supplied.
    /// </summary>
    public NutritionLookupService(IFdcNutritionClient client) : this(client, null!) { }

    /// <summary>
    /// Searches the external source for <paramref name="query"/> and returns the best match mapped to
    /// the app's catalog (per 100 g). Throws <see cref="ValidationException"/> for an empty query, and
    /// <see cref="NotFoundException"/> when neither USDA nor the app catalog yields anything usable — the
    /// client treats both as recoverable so manual entry is never blocked.
    /// </summary>
    /// <param name="userId">
    /// The caller, used to scope the offline catalog fallback to ingredients they can see. When null
    /// (the standalone constructor), the fallback is skipped.
    /// </param>
    public async Task<NutritionLookupResultDto> LookupAsync(
        string? userId, string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationException("Enter an ingredient name to look up.");
        InputLimits.LimitText(query, InputLimits.NameMaxLength, "Lookup query");
        var trimmed = query.Trim();

        var response = await _client.SearchAsync(trimmed, ct);

        // A usable USDA match wins (richest data). A null response means the source was unreachable or
        // rate-limited (the client logged why); an empty/zero-nutrient body means it had nothing useful.
        var food = response?.Foods?.FirstOrDefault(f => f.FoodNutrients is { Count: > 0 });
        if (food is not null)
        {
            var amounts = MapNutrients(food);
            if (amounts.Count > 0)
                return new NutritionLookupResultDto(
                    MatchedName: string.IsNullOrWhiteSpace(food.Description) ? trimmed : food.Description!.Trim(),
                    Source: SourceName,
                    BasisGrams: BasisGrams,
                    NutritionPer100g: amounts);
        }

        // USDA gave us nothing usable → try the app's own catalog so the button still helps offline.
        var fromCatalog = await TryCatalogFallbackAsync(userId, trimmed, ct);
        if (fromCatalog is not null)
            return fromCatalog;

        // Distinguish "source unreachable" (null) from "source returned nothing" so the message is honest.
        if (response is null)
            throw new NotFoundException(
                "Couldn't reach the live nutrition service (it may be rate-limited), and no matching " +
                "ingredient was found in your catalog. Please enter the values manually.");

        throw new NotFoundException(
            $"No nutrition match found for \"{trimmed}\". Please enter the values manually.");
    }

    /// <summary>
    /// Looks for a visible ingredient whose name or alias matches the query and, if its serving basis is
    /// a mass we can rescale, returns its nutrition expressed per 100 g. Volume/count-based ingredients
    /// can't be put on a per-100-g basis meaningfully, so they're skipped. Returns null when nothing fits.
    /// </summary>
    private async Task<NutritionLookupResultDto?> TryCatalogFallbackAsync(
        string? userId, string query, CancellationToken ct)
    {
        if (_ingredients is null || string.IsNullOrWhiteSpace(userId))
            return null;

        var candidates = await _ingredients.ListVisibleAsync(userId, query, ct);
        if (candidates.Count == 0)
            return null;

        var folded = SearchText.Fold(query);

        // Prefer an exact (folded) name match, then an exact alias match, then the first search hit.
        var match =
            candidates.FirstOrDefault(i => SearchText.Fold(i.Name) == folded)
            ?? candidates.FirstOrDefault(i => i.Aliases?.Any(a => SearchText.Fold(a) == folded) ?? false)
            ?? candidates.FirstOrDefault(i => i.NutritionPerServing.Count > 0);

        if (match is null || match.NutritionPerServing.Count == 0)
            return null;

        var info = Units.Get(match.Unit);
        if (info.Dimension != UnitDimension.Mass || match.ServingSize <= 0)
            return null; // can't express a non-mass (or zero) serving on a per-100-g basis.

        // grams in one serving = servingSize × (grams per unit); scale each amount to the 100 g basis.
        var servingGrams = match.ServingSize * info.ToBaseFactor;
        if (servingGrams <= 0)
            return null;
        var scale = BasisGrams / servingGrams;

        var per100 = match.NutritionPerServing
            .Where(a => NutrientCatalog.Find(a.NutrientId) is not null)
            .Select(a => new NutrientAmount(a.NutrientId, a.Value * scale))
            .ToList();
        if (per100.Count == 0)
            return null;

        return new NutritionLookupResultDto(
            MatchedName: match.Name,
            Source: CatalogSourceName,
            BasisGrams: BasisGrams,
            NutritionPer100g: per100);
    }

    /// <summary>
    /// Projects a USDA food's nutrient list onto catalog-keyed amounts: maps by USDA nutrient number,
    /// converts units, drops untracked nutrients, and collapses duplicates (first valid reading wins),
    /// preserving catalog order for stable, label-like output.
    /// </summary>
    public static IReadOnlyList<NutrientAmount> MapNutrients(FdcFood food)
    {
        var byId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var n in food.FoodNutrients)
        {
            var catalogId = FdcNutrientMap.ToCatalogId(n.NutrientNumber);
            if (catalogId is null) continue;
            if (byId.ContainsKey(catalogId)) continue; // first valid reading wins

            var def = NutrientCatalog.Find(catalogId);
            if (def is null) continue;

            var converted = FdcNutrientMap.ConvertValue(n.Value, n.UnitName, def);
            if (converted is null || converted < 0) continue;

            byId[catalogId] = converted.Value;
        }

        return NutrientCatalog.All
            .Where(def => byId.ContainsKey(def.Id))
            .Select(def => new NutrientAmount(def.Id, byId[def.Id]))
            .ToList();
    }
}
