using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>Ingredient as returned to clients.</summary>
public sealed record IngredientDto(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    UnitOfMeasure Unit,
    double ServingSize,
    IReadOnlyList<NutrientAmount> NutritionPerServing,
    OwnershipScope Scope,
    string? OwnerUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this ingredient.</summary>
    bool CanEdit);

/// <summary>Payload for creating or updating an ingredient.</summary>
public sealed record IngredientInput(
    string Name,
    string? Description,
    string? ImageUrl,
    UnitOfMeasure Unit,
    double ServingSize,
    IReadOnlyList<NutrientAmount>? NutritionPerServing,
    /// <summary>
    /// When true and the caller is acting as admin, the ingredient is saved at the app level.
    /// Ignored for non-admins.
    /// </summary>
    bool AppLevel = false);
