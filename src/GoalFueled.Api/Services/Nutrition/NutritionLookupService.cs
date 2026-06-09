using GoalFueled.Api.Common;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Looks up nutrition for a free-text ingredient name and maps the external source's data onto this
/// app's nutrient catalog. The external HTTP call is abstracted behind <see cref="IFdcNutritionClient"/>
/// so this mapping/parsing logic is unit-testable without any network access.
/// </summary>
public sealed class NutritionLookupService
{
    /// <summary>The amount of food the returned values describe (USDA reports per 100 g).</summary>
    public const double BasisGrams = 100d;

    public const string SourceName = "USDA FoodData Central";

    private readonly IFdcNutritionClient _client;

    public NutritionLookupService(IFdcNutritionClient client) => _client = client;

    /// <summary>
    /// Searches the external source for <paramref name="query"/> and returns the best match mapped to
    /// the app's catalog (per 100 g). Throws <see cref="ValidationException"/> for an empty query, and
    /// <see cref="NotFoundException"/> when the source is unavailable or returns nothing usable — the
    /// client treats both as recoverable so manual entry is never blocked.
    /// </summary>
    public async Task<NutritionLookupResultDto> LookupAsync(string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationException("Enter an ingredient name to look up.");
        InputLimits.LimitText(query, InputLimits.NameMaxLength, "Lookup query");

        var response = await _client.SearchAsync(query.Trim(), ct);

        // No response at all → source unreachable / rate-limited / errored (the client logged why).
        if (response is null)
            throw new NotFoundException(
                "Couldn't reach the nutrition service right now. Please enter the values manually.");

        var food = response.Foods?.FirstOrDefault(f => f.FoodNutrients is { Count: > 0 });
        if (food is null)
            throw new NotFoundException($"No nutrition match found for \"{query.Trim()}\".");

        var amounts = MapNutrients(food);
        return new NutritionLookupResultDto(
            MatchedName: string.IsNullOrWhiteSpace(food.Description) ? query.Trim() : food.Description!.Trim(),
            Source: SourceName,
            BasisGrams: BasisGrams,
            NutritionPer100g: amounts);
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
