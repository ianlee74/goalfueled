using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Contracts;

/// <summary>Ingredient as returned to clients. The search metadata lists are never null.</summary>
public sealed record IngredientDto(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    /// <summary>Product barcode (UPC/EAN), when created from a scan; null otherwise.</summary>
    string? Barcode,
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
    string? OriginalCreatorUserId,
    IngredientVisibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>True if the current caller is allowed to edit/delete this ingredient.</summary>
    bool CanEdit,
    /// <summary>True if the caller may submit this private ingredient for public review.</summary>
    bool CanSubmitForReview,
    /// <summary>True if the caller (an acting admin) may approve/reject this pending ingredient.</summary>
    bool CanReview,
    /// <summary>True when the current caller has marked this ingredient as a favorite (per-user).</summary>
    bool IsFavorite = false,
    /// <summary>True when the nutrition was verified to match the Open Food Facts record verbatim.</summary>
    bool SourcedFromOff = false,
    /// <summary>When this product was contributed to Open Food Facts; null when it never was.</summary>
    DateTimeOffset? ContributedToOffAt = null);

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
    IReadOnlyList<LocalizedName>? LocalizedNames = null,
    /// <summary>
    /// Optional product barcode (UPC/EAN). Persisted as-is; when present and the product is not yet on
    /// Open Food Facts, the new ingredient is contributed back to OFF after save.
    /// </summary>
    string? Barcode = null);
