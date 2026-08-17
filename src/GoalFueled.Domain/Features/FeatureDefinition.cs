namespace GoalFueled.Domain.Features;

/// <summary>
/// The stable keys of every feature in <see cref="FeatureCatalog"/>. Code should always reference a
/// feature through one of these constants rather than a string literal so a rename is a compile error.
/// Keys are lowercase-kebab, stable forever (they are persisted in user grant/denial lists), and are
/// compared case-insensitively.
/// </summary>
public static class FeatureKeys
{
    /// <summary>Scan a product barcode to look an item up (Open Food Facts / the local library).</summary>
    public const string BarcodeScanning = "barcode-scanning";

    /// <summary>Photograph a nutrition label and have it read into structured nutrition (vision OCR).</summary>
    public const string NutritionLabelOcr = "nutrition-label-ocr";

    /// <summary>Share your account with a doctor/trainer via single-use invitation links.</summary>
    public const string AccountSharing = "account-sharing";

    /// <summary>Build meal plans and follow one from Today.</summary>
    public const string MealPlans = "meal-plans";

    /// <summary>The "your week in fuel" weekly review and its Fuel Point bonus.</summary>
    public const string WeeklyReview = "weekly-review";

    /// <summary>Log fuel/burn by talking or typing a sentence, with the assistant filling in the details.</summary>
    public const string AiVoiceLogging = "ai-voice-logging";
}

/// <summary>
/// One entry in the feature catalog: a stable key, how to describe it, where it sits in its lifecycle,
/// and the lowest subscription tier that includes it.
/// </summary>
/// <remarks>
/// The catalog is CODE, not data: definitions ship with a build, so a feature's status or minimum tier is
/// changed by editing <see cref="FeatureCatalog"/>, never at runtime. Only the PER-USER part of access
/// (subscription level, grants, denials) is stored on the user document and editable by an admin.
/// </remarks>
public sealed record FeatureDefinition(
    string Key,
    string Name,
    string Description,
    FeatureStatus Status,
    /// <summary>The lowest tier whose subscribers get this feature. Higher tiers inherit it.</summary>
    SubscriptionLevel MinimumSubscriptionLevel = SubscriptionLevel.Free)
{
    /// <summary>
    /// Whether a subscription tier can hand this feature out at all. <see cref="FeatureStatus.Alpha"/> is
    /// deliberately excluded — an alpha feature is reachable ONLY through an explicit per-user grant.
    /// </summary>
    public bool IsIncludedBySubscription =>
        Status is FeatureStatus.Beta or FeatureStatus.Released or FeatureStatus.Deprecated;

    /// <summary>
    /// Whether the feature can be enabled for anyone at all. <see cref="FeatureStatus.Planned"/> (not built
    /// yet) and <see cref="FeatureStatus.Disabled"/> (kill switch) are off for everyone, and no per-user
    /// grant can override that.
    /// </summary>
    public bool IsEnableable => Status is not (FeatureStatus.Planned or FeatureStatus.Disabled);

    /// <summary>
    /// The tiers that include this feature, expanded from <see cref="MinimumSubscriptionLevel"/> up the
    /// ladder (empty when no tier includes it, i.e. alpha/planned/disabled features). Presented to admins
    /// so the catalog screen can show membership explicitly rather than making them reason about ordinals.
    /// </summary>
    public IReadOnlyList<SubscriptionLevel> IncludedLevels =>
        !IsIncludedBySubscription || !IsEnableable
            ? Array.Empty<SubscriptionLevel>()
            : Enum.GetValues<SubscriptionLevel>()
                .Where(l => l >= MinimumSubscriptionLevel)
                .ToArray();
}
