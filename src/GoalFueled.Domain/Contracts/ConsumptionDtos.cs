using GoalFueled.Domain;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>A single logged consumption item as returned to clients, with its computed nutrition total.</summary>
public sealed record ConsumptionEntryDto(
    string Id,
    /// <summary>Calendar day of consumption, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    ConsumptionSource Source,
    /// <summary>Referenced meal/recipe id for catalog entries; null for free-form items.</summary>
    string? SourceId,
    string Name,
    double Servings,
    /// <summary>
    /// Nutrition for this logged item (source per-serving nutrition × servings for catalog entries,
    /// or the inline free-form nutrition × servings). Empty when the source has no known nutrition.
    /// </summary>
    IReadOnlyList<NutrientAmount> Nutrition,
    /// <summary>
    /// True when this entry references a catalog meal/recipe that is no longer visible to the caller
    /// (so its live nutrition could not be resolved); the stored <see cref="Name"/> still shows.
    /// </summary>
    bool SourceMissing,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A day's consumption log: its entries plus the rolled-up nutrition totals across them.</summary>
public sealed record ConsumptionDayDto(
    /// <summary>The day this log is for, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    IReadOnlyList<ConsumptionEntryDto> Entries,
    /// <summary>Sum of every entry's nutrition for the day (the same way nutrition is aggregated elsewhere).</summary>
    IReadOnlyList<NutrientAmount> TotalNutrition);

/// <summary>
/// Payload for logging a consumed item. Either reference the catalog (set <see cref="Source"/> to
/// <c>Meal</c> or <c>Recipe</c> with a <see cref="SourceId"/>) so nutrition is computed from it, or log
/// a free-form item (<see cref="Source"/> = <c>FreeForm</c>) with a <see cref="Name"/> and optional
/// inline per-serving <see cref="Nutrition"/>. The date is optional and defaults to today.
/// </summary>
public sealed record ConsumptionEntryInput(
    ConsumptionSource Source,
    /// <summary>Required for Meal/Recipe sources: the catalog item's id. Ignored for free-form entries.</summary>
    string? SourceId = null,
    /// <summary>Required for free-form entries; optional override of the snapshot name for catalog entries.</summary>
    string? Name = null,
    /// <summary>Servings consumed (defaults to 1). Must be greater than zero.</summary>
    double Servings = 1,
    /// <summary>Optional per-serving nutrition for a free-form entry. Ignored for catalog entries.</summary>
    IReadOnlyList<NutrientAmount>? Nutrition = null,
    /// <summary>Defaults to today (the request date) when omitted.</summary>
    DateOnly? Date = null);
