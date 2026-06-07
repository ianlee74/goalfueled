namespace GoalFueled.Domain.Entities;

/// <summary>
/// A plan for when to consume daily meals, either a single day (<see cref="MealPlanCadence.Daily"/>)
/// or a full week (<see cref="MealPlanCadence.Weekly"/>). Scheduled meals may be pinned to a time;
/// untimed items (e.g. "drink water through the day") have no specific time.
/// </summary>
public class MealPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string? OwnerUserId { get; set; }

    public MealPlanCadence Cadence { get; set; } = MealPlanCadence.Daily;

    /// <summary>Meals scheduled for specific times/days.</summary>
    public List<ScheduledMeal> ScheduledMeals { get; set; } = new();

    /// <summary>Meals or ingredients to consume during the day without a specific time.</summary>
    public List<UntimedItem> UntimedItems { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A meal scheduled within a plan, optionally pinned to a day of week and time of day.</summary>
public class ScheduledMeal
{
    public string MealId { get; set; } = string.Empty;

    /// <summary>Day of week for weekly plans; null for daily plans (applies every day).</summary>
    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>Time of day to consume the meal; null if unspecified.</summary>
    public TimeOnly? Time { get; set; }
}

/// <summary>An item consumed during the day with no specific time, referencing a meal or an ingredient.</summary>
public class UntimedItem
{
    /// <summary>Set when the item is a meal.</summary>
    public string? MealId { get; set; }

    /// <summary>Set when the item is a standalone ingredient (e.g. water).</summary>
    public string? IngredientId { get; set; }

    /// <summary>Quantity for ingredient items, in the ingredient's unit; null for meals.</summary>
    public double? Quantity { get; set; }

    public string? Note { get; set; }
}
