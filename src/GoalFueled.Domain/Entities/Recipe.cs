namespace GoalFueled.Domain.Entities;

/// <summary>
/// A collection of ingredients combined to make a meal. May be app-level (admin authored) or
/// user-owned. A user-owned recipe can be submitted for review to become public; once
/// <see cref="RecipeVisibility.Public"/> it can never return to private. The original author
/// is always credited via <see cref="OriginalCreatorUserId"/>.
/// </summary>
public class Recipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Preparation notes / description; may contain HTML.</summary>
    public string? Description { get; set; }

    /// <summary>Image of the prepared recipe.</summary>
    public string? ImageUrl { get; set; }

    public List<RecipeItem> Items { get; set; } = new();

    /// <summary>How many servings the full recipe yields, used to derive per-serving nutrition.</summary>
    public double Servings { get; set; } = 1;

    public OwnershipScope Scope { get; set; } = OwnershipScope.User;

    public string? OwnerUserId { get; set; }

    /// <summary>The user credited with creating this recipe; retained even after it becomes public.</summary>
    public string? OriginalCreatorUserId { get; set; }

    public RecipeVisibility Visibility { get; set; } = RecipeVisibility.Private;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A quantity of one ingredient within a recipe.</summary>
public class RecipeItem
{
    public string IngredientId { get; set; } = string.Empty;

    /// <summary>Quantity of the ingredient, expressed in <see cref="Unit"/>.</summary>
    public double Quantity { get; set; }

    /// <summary>Unit the quantity is expressed in (must be dimension-compatible with the ingredient's unit).</summary>
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;
}
