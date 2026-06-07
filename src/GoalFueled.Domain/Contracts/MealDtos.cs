using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>One portion of a prepared recipe within a meal, as returned to clients.</summary>
public sealed record MealPortionDto(
    string RecipeId,
    double Servings,
    /// <summary>Resolved recipe name (null if the recipe is no longer visible/exists).</summary>
    string? RecipeName);

/// <summary>Meal as returned to clients, including its rolled-up nutrition.</summary>
public sealed record MealDto(
    string Id,
    string Name,
    IReadOnlyList<MealPortionDto> Portions,
    OwnershipScope Scope,
    string? OwnerUserId,
    /// <summary>Nutrition for the whole meal (sum over portions of servings × recipe per-serving nutrition).</summary>
    IReadOnlyList<NutrientAmount> TotalNutrition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this meal.</summary>
    bool CanEdit);

/// <summary>One portion line in a create/update payload.</summary>
public sealed record MealPortionInput(
    string RecipeId,
    double Servings);

/// <summary>Payload for creating or updating a meal.</summary>
public sealed record MealInput(
    string Name,
    IReadOnlyList<MealPortionInput>? Portions,
    /// <summary>
    /// When true and the caller is acting as admin, the meal is saved at the app level.
    /// Ignored for non-admins.
    /// </summary>
    bool AppLevel = false);
