using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>A meal scheduled within a plan, as returned to clients (with the resolved meal name).</summary>
public sealed record ScheduledMealDto(
    string MealId,
    /// <summary>Day of week for weekly plans; null for daily plans (applies every day).</summary>
    DayOfWeek? DayOfWeek,
    /// <summary>Time of day to consume the meal, "HH:mm:ss"; null if unspecified.</summary>
    TimeOnly? Time,
    /// <summary>Resolved meal name (null if the meal is no longer visible/exists).</summary>
    string? MealName);

/// <summary>An untimed item (a meal or a standalone ingredient) as returned to clients.</summary>
public sealed record UntimedItemDto(
    string? MealId,
    string? IngredientId,
    double? Quantity,
    string? Note,
    /// <summary>Resolved name of the referenced meal or ingredient (null if missing/not visible).</summary>
    string? Name);

/// <summary>
/// Nutrition consumed on a single day of a plan. For a daily plan there is one of these (with a null
/// <see cref="DayOfWeek"/>); for a weekly plan there is one per day of week that has any consumption.
/// </summary>
public sealed record DailyNutritionDto(
    /// <summary>The day this summary covers; null for a daily plan's single all-day summary.</summary>
    DayOfWeek? DayOfWeek,
    IReadOnlyList<NutrientAmount> Nutrition);

/// <summary>
/// The plan's goal-tracking summary. A daily plan reports one <see cref="DailyNutritionDto"/>; a weekly
/// plan reports one per day plus the <see cref="WeeklyTotal"/> and <see cref="DailyAverage"/> across the week.
/// </summary>
public sealed record NutritionSummaryDto(
    /// <summary>Per-day breakdown. One entry for daily plans; up to seven for weekly plans.</summary>
    IReadOnlyList<DailyNutritionDto> Days,
    /// <summary>Sum across all days (equals the single day for a daily plan).</summary>
    IReadOnlyList<NutrientAmount> WeeklyTotal,
    /// <summary>Average per day across the week (weekly total / 7); equals the single day for a daily plan.</summary>
    IReadOnlyList<NutrientAmount> DailyAverage);

/// <summary>Meal plan as returned to clients, including its resolved items and nutrition summary.</summary>
public sealed record MealPlanDto(
    string Id,
    string Name,
    MealPlanCadence Cadence,
    string? OwnerUserId,
    IReadOnlyList<ScheduledMealDto> ScheduledMeals,
    IReadOnlyList<UntimedItemDto> UntimedItems,
    /// <summary>Daily/weekly nutrition roll-up of all scheduled meals and untimed items.</summary>
    NutritionSummaryDto Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this plan.</summary>
    bool CanEdit);

/// <summary>One scheduled-meal line in a create/update payload.</summary>
public sealed record ScheduledMealInput(
    string MealId,
    DayOfWeek? DayOfWeek = null,
    TimeOnly? Time = null);

/// <summary>One untimed-item line in a create/update payload (a meal OR an ingredient).</summary>
public sealed record UntimedItemInput(
    string? MealId = null,
    string? IngredientId = null,
    double? Quantity = null,
    string? Note = null);

/// <summary>Payload for creating or updating a meal plan.</summary>
public sealed record MealPlanInput(
    string Name,
    MealPlanCadence Cadence,
    IReadOnlyList<ScheduledMealInput>? ScheduledMeals = null,
    IReadOnlyList<UntimedItemInput>? UntimedItems = null);
