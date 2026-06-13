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
    /// <summary>How many servings the whole meal yields (null/old meals are treated as 1).</summary>
    double Servings,
    OwnershipScope Scope,
    string? OwnerUserId,
    /// <summary>Nutrition for the whole meal (sum over portions of servings × recipe per-serving nutrition).</summary>
    IReadOnlyList<NutrientAmount> TotalNutrition,
    /// <summary>Nutrition per single serving of the meal (total / Servings).</summary>
    IReadOnlyList<NutrientAmount> NutritionPerServing,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this meal.</summary>
    bool CanEdit,
    /// <summary>True when the current caller has marked this meal as a favorite (per-user).</summary>
    bool IsFavorite = false);

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
    bool AppLevel = false,
    /// <summary>How many servings the whole meal yields; must be greater than zero. Defaults to 1.</summary>
    double Servings = 1);
