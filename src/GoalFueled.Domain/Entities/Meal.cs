namespace GoalFueled.Domain.Entities;

/// <summary>A collection of portions of prepared recipes eaten together as one meal.</summary>
public class Meal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<MealPortion> Portions { get; set; } = new();

    /// <summary>
    /// How many servings the whole meal yields, used to derive per-serving nutrition (total / Servings).
    /// Nullable so old persisted meals (which predate this field) still load; a null or non-positive
    /// value is treated as 1 serving (per-serving == total).
    /// </summary>
    public double? Servings { get; set; } = 1;

    /// <summary>
    /// What kind of fuel this source is (food / supplement / hydration). Nullable so meals persisted
    /// before this field existed still load — a non-nullable value-type field throws "Nullable object
    /// must have a value" on read for old Cosmos documents. A null read is treated as
    /// <see cref="FuelType.Food"/> (the sensible default) when mapped to the DTO; new fuel sources
    /// default to <see cref="FuelType.Food"/> on the create path.
    /// </summary>
    public FuelType? FuelType { get; set; } = Domain.FuelType.Food;

    public OwnershipScope Scope { get; set; } = OwnershipScope.User;

    public string? OwnerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A portion of one prepared recipe within a meal, measured in recipe servings.</summary>
public class MealPortion
{
    public string RecipeId { get; set; } = string.Empty;

    /// <summary>Number of recipe servings included (e.g. 0.5 = half a serving).</summary>
    public double Servings { get; set; } = 1;
}
