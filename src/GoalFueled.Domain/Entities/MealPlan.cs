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
    /// <summary>
    /// The slot's default meal. When the slot offers options (see <see cref="AlternativeMealIds"/>)
    /// this is the one the plan's nutrition summary uses and the one logged when the user completes the
    /// task without choosing an alternative. It is also the slot's stable identity in a planned-meal ref.
    /// </summary>
    public string MealId { get; set; } = string.Empty;

    /// <summary>
    /// Alternative meals the user may pick instead of <see cref="MealId"/> when this slot is consumed.
    /// Empty for a single-meal slot. The full option set a user chooses from is
    /// <c>[MealId, ..AlternativeMealIds]</c>; the default never appears in this list.
    /// <para>Coalesces to an empty list on read: plan documents written before this field existed
    /// materialize the property as null (the Cosmos provider does not run the field initializer for an
    /// absent JSON property), so a null read must never surface to callers — mirroring how the nullable
    /// <c>FuelType</c> read resolves to a default rather than throwing.</para>
    /// </summary>
    private List<string>? _alternativeMealIds;
    public List<string> AlternativeMealIds
    {
        get => _alternativeMealIds ??= new();
        set => _alternativeMealIds = value;
    }

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
