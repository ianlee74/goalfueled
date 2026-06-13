using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>Ingredient as returned to clients. The search metadata lists are never null.</summary>
public sealed record IngredientDto(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    /// <summary>Free-form search/browse tags (e.g. "protein", "vegetable").</summary>
    IReadOnlyList<string> Tags,
    /// <summary>Alternate names matched in search (e.g. "garbanzo beans").</summary>
    IReadOnlyList<string> Aliases,
    /// <summary>Non-English names matched in search.</summary>
    IReadOnlyList<LocalizedName> LocalizedNames,
    UnitOfMeasure Unit,
    double ServingSize,
    IReadOnlyList<NutrientAmount> NutritionPerServing,
    OwnershipScope Scope,
    string? OwnerUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this ingredient.</summary>
    bool CanEdit,
    /// <summary>True when the current caller has marked this ingredient as a favorite (per-user).</summary>
    bool IsFavorite = false);

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
    bool AppLevel = false,
    /// <summary>Optional search/browse tags; entries are trimmed and de-duplicated on save.</summary>
    IReadOnlyList<string>? Tags = null,
    /// <summary>Optional alternate names; entries are trimmed and de-duplicated on save.</summary>
    IReadOnlyList<string>? Aliases = null,
    /// <summary>Optional non-English names; entries are trimmed and de-duplicated on save.</summary>
    IReadOnlyList<LocalizedName>? LocalizedNames = null);
