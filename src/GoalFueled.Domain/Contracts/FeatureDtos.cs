namespace GoalFueled.Domain.Contracts;

/// <summary>
/// A feature tag as shown on the admin catalog screen: what it is, where it sits in its lifecycle, and
/// which subscription tiers include it. The catalog ships with the build, so this is read-only reference
/// data — only the PER-USER part of access (tier, grants, denials) is editable.
/// </summary>
public sealed record FeatureDto(
    string Key,
    string Name,
    string Description,
    FeatureStatus Status,
    /// <summary>The lowest tier that includes this feature; higher tiers inherit it.</summary>
    SubscriptionLevel MinimumSubscriptionLevel,
    /// <summary>
    /// The tiers that include it, expanded from the minimum. Empty for features no tier hands out
    /// (alpha = grant-only, planned/disabled = nobody).
    /// </summary>
    IReadOnlyList<SubscriptionLevel> IncludedLevels);

/// <summary>
/// One feature the signed-in user actually HAS, returned on <see cref="CurrentUserDto"/>. Carries the
/// status alongside the key so the client can badge a gated surface "Beta" without a second round trip.
/// </summary>
public sealed record UserFeatureDto(string Key, string Name, FeatureStatus Status);

/// <summary>Payload for an admin moving a user to a different subscription tier.</summary>
public sealed record SetSubscriptionLevelInput(SubscriptionLevel Level);

/// <summary>
/// Payload for an admin setting one user's individual override for one feature.
/// <see cref="FeatureAccessMode.Inherit"/> clears the override so the tier decides again.
/// </summary>
public sealed record SetFeatureAccessInput(string Key, FeatureAccessMode Mode);
