using System.Collections.ObjectModel;

namespace GoalFueled.Domain.Features;

/// <summary>
/// The canonical list of features the app can gate ("feature tags"), modeled the same way as
/// <see cref="Nutrition.NutrientCatalog"/>: static reference data that ships with the build. Adding a
/// feature means adding a <see cref="FeatureKeys"/> constant and one row here.
/// </summary>
/// <remarks>
/// <para>
/// Tiering policy: shipped capabilities are deliberately left at <see cref="SubscriptionLevel.Free"/> so
/// introducing the flag system takes nothing away from existing accounts — the one exception is nutrition
/// label OCR, whose per-use vision-API cost makes it the natural paid-tier feature. Moving any other
/// feature behind a tier is a one-line edit here (change its
/// <see cref="FeatureDefinition.MinimumSubscriptionLevel"/>) — a product decision, not a plumbing one.
/// </para>
/// </remarks>
public static class FeatureCatalog
{
    /// <summary>All known features, ordered roughly by how prominent they are in the product.</summary>
    public static readonly ReadOnlyCollection<FeatureDefinition> All = new(new List<FeatureDefinition>
    {
        new(FeatureKeys.BarcodeScanning,
            "Barcode scanning",
            "Scan a product barcode to pull an item straight from your library or Open Food Facts.",
            FeatureStatus.Beta),

        // The one shipped capability placed above Free: every OCR read costs a metered vision-API call, so
        // it is the natural paid-tier feature. Free accounts that need it can still be granted it
        // individually by an admin (that is exactly what per-user grants are for).
        new(FeatureKeys.NutritionLabelOcr,
            "Nutrition label OCR",
            "Photograph a nutrition label and have its panel read into structured nutrition for you.",
            FeatureStatus.Beta,
            SubscriptionLevel.Plus),

        new(FeatureKeys.AccountSharing,
            "Account sharing",
            "Share your log with a coach or clinician through a single-use invitation link.",
            FeatureStatus.Released),

        new(FeatureKeys.MealPlans,
            "Meal plans",
            "Build daily or weekly plans and follow one from Today as completable tasks.",
            FeatureStatus.Released),

        new(FeatureKeys.WeeklyReview,
            "Weekly review",
            "Your week in fuel: week score, streaks, and the Fuel Point bonus for completing it.",
            FeatureStatus.Released),

        // Shipping as Beta. Kept at Pro for the same reason label OCR sits at Plus, only more so: a single
        // request can fan out into several billed model calls, so it is the most expensive per-use
        // capability in the app. Free/Plus accounts that need it can still be granted it individually.
        new(FeatureKeys.AiVoiceLogging,
            "AI voice logging",
            "Say or type what you ate and have the assistant create the recipe and log it for you.",
            FeatureStatus.Beta,
            SubscriptionLevel.Pro),
    });

    /// <summary>Case-insensitive key → definition index.</summary>
    private static readonly Dictionary<string, FeatureDefinition> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The definition for <paramref name="key"/>, or null when the key is not in the catalog.</summary>
    public static FeatureDefinition? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : ByKey.GetValueOrDefault(key.Trim());

    /// <summary>True when <paramref name="key"/> names a feature in the catalog (case-insensitively).</summary>
    public static bool IsKnown(string? key) => Find(key) is not null;

    /// <summary>
    /// The catalog's canonical spelling of <paramref name="key"/>, so stored grants/denials never drift
    /// in casing or whitespace. Returns null for unknown keys.
    /// </summary>
    public static string? Normalize(string? key) => Find(key)?.Key;
}
