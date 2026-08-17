using GoalFueled.Domain.Entities;

namespace GoalFueled.Domain.Features;

/// <summary>
/// Resolves which catalog features a given user actually has, and edits the per-user part of that
/// (grants/denials) on the user document.
/// </summary>
/// <remarks>
/// <para><b>Resolution semantics</b> — for each feature in <see cref="FeatureCatalog.All"/>:</para>
/// <list type="number">
/// <item>A <see cref="FeatureStatus.Planned"/> or <see cref="FeatureStatus.Disabled"/> feature is off for
///   everyone. This is checked FIRST so a stale per-user grant can never resurrect a killed feature.</item>
/// <item>An individual DENIAL wins over everything else (tier membership and an individual grant).</item>
/// <item>Otherwise the feature is on when the user's subscription tier includes it
///   (<c>level &gt;= MinimumSubscriptionLevel</c>, and the status permits tier inclusion at all), OR the
///   user has been individually GRANTED it.</item>
/// </list>
/// <para>
/// In set terms: <c>effective = (tier ∪ granted) \ denied</c>, intersected with the enableable catalog.
/// </para>
/// <para><b>Legacy documents</b> — all three per-user fields on <see cref="AppUser"/> are nullable (the
/// project's Cosmos non-nullable-field rule). A null subscription level reads as
/// <see cref="SubscriptionLevel.Free"/> and null grant/denial lists read as empty, so a user document
/// written before this feature existed resolves to exactly "the free tier, no overrides".</para>
/// <para><b>Sharing</b> — when a guest is acting on someone else's account via a share, the
/// <see cref="AppUser"/> in hand IS the shared account, so the OWNER's tier and grants apply. A guest
/// never brings their own subscription into an account they were let into.</para>
/// </remarks>
public static class FeatureAccess
{
    /// <summary>Keys are compared case-insensitively everywhere so stored casing can never matter.</summary>
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Every catalog feature the user currently has, in catalog order. Definitions (not just keys) so
    /// callers can surface the name/status without a second lookup.
    /// </summary>
    public static IReadOnlyList<FeatureDefinition> Resolve(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var level = user.EffectiveSubscriptionLevel;
        var granted = ToSet(user.GrantedFeatureKeys);
        var denied = ToSet(user.DeniedFeatureKeys);

        var result = new List<FeatureDefinition>();
        foreach (var feature in FeatureCatalog.All)
        {
            // 1. Never-on statuses come first: no grant can override a planned or killed feature.
            if (!feature.IsEnableable) continue;

            // 2. An individual denial beats the tier and any individual grant.
            if (denied.Contains(feature.Key)) continue;

            // 3. Included by the tier, or handed to this user specifically.
            var bySubscription = feature.IsIncludedBySubscription && level >= feature.MinimumSubscriptionLevel;
            if (bySubscription || granted.Contains(feature.Key))
                result.Add(feature);
        }

        return result;
    }

    /// <summary>The keys of <see cref="Resolve"/>, for payloads/tests that only care about membership.</summary>
    public static IReadOnlyList<string> ResolveKeys(AppUser user) =>
        Resolve(user).Select(f => f.Key).ToList();

    /// <summary>
    /// Whether the user has <paramref name="featureKey"/>. Unknown keys are always false — a typo can
    /// never accidentally open a gate.
    /// </summary>
    public static bool IsEnabled(AppUser user, string featureKey)
    {
        var feature = FeatureCatalog.Find(featureKey);
        return feature is not null && Resolve(user).Any(f => f.Key == feature.Key);
    }

    /// <summary>The user's individual override for a feature (ignoring what their tier includes).</summary>
    public static FeatureAccessMode GetOverride(AppUser user, string featureKey)
    {
        ArgumentNullException.ThrowIfNull(user);
        var key = FeatureCatalog.Normalize(featureKey) ?? featureKey?.Trim() ?? string.Empty;
        if (ToSet(user.DeniedFeatureKeys).Contains(key)) return FeatureAccessMode.Denied;
        if (ToSet(user.GrantedFeatureKeys).Contains(key)) return FeatureAccessMode.Granted;
        return FeatureAccessMode.Inherit;
    }

    /// <summary>
    /// Sets (or clears, with <see cref="FeatureAccessMode.Inherit"/>) the user's individual override for a
    /// feature. Grant and denial are mutually exclusive — writing one always removes the other. The caller
    /// is responsible for authorization and for having validated the key against the catalog.
    /// </summary>
    /// <remarks>
    /// Both lists collapse back to <c>null</c> when they end up empty, matching how
    /// <see cref="AppUser.FavoriteIngredientIds"/> is handled: a user who has no overrides keeps a document
    /// with no such property at all, so nothing is written for the overwhelmingly common case.
    /// </remarks>
    public static void SetOverride(AppUser user, string featureKey, FeatureAccessMode mode)
    {
        ArgumentNullException.ThrowIfNull(user);
        var key = FeatureCatalog.Normalize(featureKey)
            ?? throw new ArgumentException($"Unknown feature '{featureKey}'.", nameof(featureKey));

        user.GrantedFeatureKeys = Without(user.GrantedFeatureKeys, key);
        user.DeniedFeatureKeys = Without(user.DeniedFeatureKeys, key);

        switch (mode)
        {
            case FeatureAccessMode.Granted:
                user.GrantedFeatureKeys = With(user.GrantedFeatureKeys, key);
                break;
            case FeatureAccessMode.Denied:
                user.DeniedFeatureKeys = With(user.DeniedFeatureKeys, key);
                break;
        }
    }

    /// <summary>Null-safe, case-insensitive view of one of the stored key lists.</summary>
    private static HashSet<string> ToSet(IEnumerable<string>? keys) =>
        keys is null ? new HashSet<string>(KeyComparer) : new HashSet<string>(keys, KeyComparer);

    /// <summary>Removes a key, collapsing an emptied list back to null so old documents stay lean.</summary>
    private static List<string>? Without(List<string>? keys, string key)
    {
        if (keys is null) return null;
        var kept = keys.Where(k => !KeyComparer.Equals(k, key)).ToList();
        return kept.Count == 0 ? null : kept;
    }

    /// <summary>Adds a key to a (possibly null) list, materializing the list only when needed.</summary>
    private static List<string> With(List<string>? keys, string key)
    {
        var list = keys ?? new List<string>();
        list.Add(key);
        return list;
    }
}
