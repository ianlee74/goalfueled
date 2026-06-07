namespace GoalFueled.Domain.Entities;

/// <summary>A collection of portions of prepared recipes eaten together as one meal.</summary>
public class Meal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<MealPortion> Portions { get; set; } = new();

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
