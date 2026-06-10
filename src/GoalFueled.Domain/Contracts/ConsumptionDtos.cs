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
    DateTimeOffset UpdatedAt,
    /// <summary>
    /// Correlation key when this entry came from completing a planned-meal task; null for ad-hoc entries.
    /// Lets The Log pair an entry with the plan task it satisfies.
    /// </summary>
    string? PlannedMealRef = null);

/// <summary>A day's consumption log: its entries plus the rolled-up nutrition totals across them.</summary>
public sealed record ConsumptionDayDto(
    /// <summary>The day this log is for, serialized as "yyyy-MM-dd".</summary>
    DateOnly Date,
    IReadOnlyList<ConsumptionEntryDto> Entries,
    /// <summary>Sum of every entry's nutrition for the day (the same way nutrition is aggregated elsewhere).</summary>
    IReadOnlyList<NutrientAmount> TotalNutrition);

/// <summary>
/// One planned-meal task for a day in The Log: a meal scheduled by the active plan for that day, shown as
/// a checkable item. Carries the resolved meal name, its scheduled time (if any), and its nutrition (the
/// same meal-total roll-up the plan summary uses). <see cref="Done"/> reflects whether a completion entry
/// exists; when it does, <see cref="CompletedEntryId"/> is the id of that <see cref="ConsumptionEntryDto"/>
/// (used to un-complete the task).
/// </summary>
public sealed record PlannedMealTaskDto(
    /// <summary>Stable correlation key for this task on this date (<c>planId:mealId:day:time</c>).</summary>
    string Ref,
    string MealId,
    /// <summary>Resolved meal name (null if the meal is no longer visible/exists).</summary>
    string? MealName,
    /// <summary>Scheduled time of day, "HH:mm:ss"; null for an untimed/anytime task.</summary>
    TimeOnly? Time,
    IReadOnlyList<NutrientAmount> Nutrition,
    bool Done,
    /// <summary>Id of the consumption entry that completed this task, when <see cref="Done"/> is true.</summary>
    string? CompletedEntryId,
    /// <summary>
    /// True when the user has <em>skipped</em> this planned meal (a skip entry exists for its ref). A skipped
    /// task is neither <see cref="Done"/> nor pending; it is dropped from the "still to do" list and adds no
    /// nutrition. Mutually exclusive with <see cref="Done"/> in practice (one entry per ref). Trailing optional
    /// so existing positional constructions keep working.
    /// </summary>
    bool Skipped = false);

/// <summary>
/// The active plan's tasks for a single day, surfaced in The Log. Empty <see cref="Tasks"/> with a null
/// <see cref="PlanId"/> means the user has no active plan (or it has no meals scheduled for the day).
/// </summary>
public sealed record PlannedDayDto(
    /// <summary>The day, "yyyy-MM-dd".</summary>
    DateOnly Date,
    /// <summary>The active plan's id, or null when none is selected.</summary>
    string? PlanId,
    string? PlanName,
    IReadOnlyList<PlannedMealTaskDto> Tasks,
    /// <summary>
    /// The recurring "Log your weight" task for this day, present only when the user's weight schedule
    /// makes a measurement due on the date; null otherwise (so the task appears only on due days).
    /// </summary>
    WeightTaskDto? WeightTask = null);

/// <summary>
/// The recurring "Log your weight" task in The Log on a day a measurement is due. Completion reuses the
/// existing one-per-day weight model: <see cref="Done"/> is true iff a <see cref="WeightEntryDto"/> exists
/// for the date, and <see cref="RecordedValue"/>/<see cref="RecordedUnit"/> echo that entry for display.
/// </summary>
public sealed record WeightTaskDto(
    bool Done,
    /// <summary>The recorded weight for the day when <see cref="Done"/>; null otherwise.</summary>
    double? RecordedValue,
    /// <summary>The recorded weight's unit when <see cref="Done"/>; null otherwise.</summary>
    WeightUnit? RecordedUnit);

/// <summary>
/// Payload for completing a planned-meal task from The Log. The <see cref="Ref"/> is the task's stable
/// correlation key (from <see cref="PlannedMealTaskDto.Ref"/>); the server resolves the meal and date
/// from the active plan, so the client never has to restate nutrition.
/// </summary>
public sealed record CompletePlannedMealInput(
    string Ref,
    /// <summary>Defaults to today when omitted.</summary>
    DateOnly? Date = null);

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
