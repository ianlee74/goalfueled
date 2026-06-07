using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Entities;

/// <summary>
/// The most basic component of a recipe. Can be authored at the app level by an admin
/// (<see cref="OwnershipScope.App"/>) or privately by a user (<see cref="OwnershipScope.User"/>).
/// Nutrition is expressed per one <see cref="ServingSize"/> of <see cref="Unit"/>.
/// </summary>
public class Ingredient
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Description; may contain HTML-formatted text (sanitized before display).</summary>
    public string? Description { get; set; }

    /// <summary>URL of an uploaded image in blob storage, if any.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>The unit used to define a single serving of this ingredient.</summary>
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;

    /// <summary>How many <see cref="Unit"/> make up the serving the nutrition values describe (commonly 1 or 100).</summary>
    public double ServingSize { get; set; } = 100;

    /// <summary>Nutritional values for one serving (i.e. <see cref="ServingSize"/> of <see cref="Unit"/>).</summary>
    public List<NutrientAmount> NutritionPerServing { get; set; } = new();

    public OwnershipScope Scope { get; set; } = OwnershipScope.User;

    /// <summary>Owner user id for user-scoped ingredients; null for app-scoped ingredients.</summary>
    public string? OwnerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
