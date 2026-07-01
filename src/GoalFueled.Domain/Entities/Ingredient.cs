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

    /// <summary>
    /// Product barcode (UPC/EAN), when this ingredient was created from a scanned package. Used to
    /// de-duplicate re-scans and to contribute the product back to Open Food Facts. Nullable: legacy
    /// documents written before this field existed must materialize as null rather than throw (see the
    /// Cosmos non-nullable-field landmine).
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>The unit used to define a single serving of this ingredient.</summary>
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Gram;

    /// <summary>How many <see cref="Unit"/> make up the serving the nutrition values describe (commonly 1 or 100).</summary>
    public double ServingSize { get; set; } = 100;

    /// <summary>Nutritional values for one serving (i.e. <see cref="ServingSize"/> of <see cref="Unit"/>).</summary>
    public List<NutrientAmount> NutritionPerServing { get; set; } = new();

    /// <summary>
    /// Free-form search/browse tags (e.g. "protein", "vegetable", "breakfast"). Matched by the
    /// ingredient finder. Nullable: documents written before this field existed have no such
    /// property and must materialize as null rather than throw.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Alternate names the ingredient is commonly known by (e.g. "garbanzo beans" for chickpeas).
    /// Matched by the ingredient finder. Nullable for the same old-document reason as <see cref="Tags"/>.
    /// </summary>
    public List<string>? Aliases { get; set; }

    /// <summary>
    /// Non-English names, each tagged with a language code, matched by the ingredient finder
    /// (e.g. es → "garbanzos"). Nullable for the same old-document reason as <see cref="Tags"/>.
    /// </summary>
    public List<LocalizedName>? LocalizedNames { get; set; }

    public OwnershipScope Scope { get; set; } = OwnershipScope.User;

    /// <summary>Owner user id for user-scoped ingredients; null for app-scoped ingredients.</summary>
    public string? OwnerUserId { get; set; }

    /// <summary>
    /// Public-visibility lifecycle for the ingredient review workflow (mirrors recipes). Nullable so
    /// documents written before this field existed materialize as null rather than throwing on read
    /// (Cosmos non-nullable value-type landmine). Interpret null via <see cref="EffectiveVisibility"/>:
    /// an app-scoped ingredient is inherently <see cref="IngredientVisibility.Public"/>, a user one is
    /// <see cref="IngredientVisibility.Private"/>.
    /// </summary>
    public IngredientVisibility? Visibility { get; set; }

    /// <summary>
    /// The user the ingredient should be credited to once public; retained after approval so the
    /// original author keeps credit even though the ingredient stays user-scoped/owned. Nullable for
    /// the same old-document reason as <see cref="Visibility"/>.
    /// </summary>
    public string? OriginalCreatorUserId { get; set; }

    /// <summary>
    /// Resolves <see cref="Visibility"/> to a concrete value, treating a null (legacy) value as the
    /// scope-appropriate default: app-scoped ingredients are public; user-scoped are private.
    /// </summary>
    public IngredientVisibility EffectiveVisibility =>
        Visibility ?? (Scope == OwnershipScope.App ? IngredientVisibility.Public : IngredientVisibility.Private);

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A non-English name for an ingredient, stored as an owned collection item (like
/// <see cref="NutrientAmount"/>) so it serializes naturally into the document.
/// </summary>
public sealed class LocalizedName
{
    /// <summary>Lower-case ISO 639-1 language code (e.g. "es", "fr", "zh").</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The ingredient's name in that language.</summary>
    public string Name { get; set; } = string.Empty;

    public LocalizedName() { }

    public LocalizedName(string language, string name)
    {
        Language = language;
        Name = name;
    }
}
