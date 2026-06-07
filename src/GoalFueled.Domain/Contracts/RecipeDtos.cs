using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>One ingredient line of a recipe, as returned to clients.</summary>
public sealed record RecipeItemDto(
    string IngredientId,
    double Quantity,
    UnitOfMeasure Unit,
    /// <summary>Resolved ingredient name (null if the ingredient is no longer visible/exists).</summary>
    string? IngredientName);

/// <summary>Recipe as returned to clients, including its rolled-up nutrition.</summary>
public sealed record RecipeDto(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    IReadOnlyList<RecipeItemDto> Items,
    double Servings,
    OwnershipScope Scope,
    string? OwnerUserId,
    string? OriginalCreatorUserId,
    RecipeVisibility Visibility,
    /// <summary>Nutrition for the whole recipe (sum of all ingredient contributions).</summary>
    IReadOnlyList<NutrientAmount> TotalNutrition,
    /// <summary>Nutrition per single serving (total / Servings).</summary>
    IReadOnlyList<NutrientAmount> NutritionPerServing,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this recipe.</summary>
    bool CanEdit,
    /// <summary>True if the caller may submit this private recipe for public review.</summary>
    bool CanSubmitForReview,
    /// <summary>True if the caller (an acting admin) may approve/reject this pending recipe.</summary>
    bool CanReview);

/// <summary>One ingredient line in a create/update payload.</summary>
public sealed record RecipeItemInput(
    string IngredientId,
    double Quantity,
    UnitOfMeasure Unit);

/// <summary>Payload for creating or updating a recipe.</summary>
public sealed record RecipeInput(
    string Name,
    string? Description,
    string? ImageUrl,
    IReadOnlyList<RecipeItemInput>? Items,
    double Servings,
    /// <summary>
    /// When true and the caller is acting as admin, the recipe is saved at the app level.
    /// Ignored for non-admins.
    /// </summary>
    bool AppLevel = false);
