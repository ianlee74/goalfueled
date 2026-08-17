using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;

namespace GoalFueled.Api.Common;

/// <summary>
/// Authorization guard for feature tags — the feature-flag equivalent of the <c>EnsureActingAdmin</c>
/// checks the services already use. A service or Function that implements a gated capability calls
/// <see cref="EnsureFeature"/> at the top and gets the standard 403 for free.
/// </summary>
/// <example>
/// <code>
/// FeatureGuard.EnsureFeature(user, FeatureKeys.BarcodeScanning);
/// </code>
/// </example>
public static class FeatureGuard
{
    /// <summary>
    /// Throws <see cref="ForbiddenException"/> (403) unless the account being operated on has the feature.
    /// Fails closed: an unknown/misspelled key is never enabled, so a typo blocks rather than opens.
    /// </summary>
    /// <remarks>
    /// The user passed in is the EFFECTIVE account (the shared account when a guest is acting via a share),
    /// so a gated capability follows the tier and grants of the account whose data is being touched.
    /// </remarks>
    public static void EnsureFeature(AppUser user, string featureKey)
    {
        if (Has(user, featureKey)) return;

        var name = FeatureCatalog.Find(featureKey)?.Name;
        throw new ForbiddenException(name is null
            ? "This feature is not available."
            : $"{name} isn't enabled for this account.");
    }

    /// <summary>Non-throwing form, for endpoints that want to degrade rather than fail.</summary>
    public static bool Has(AppUser user, string featureKey) => FeatureAccess.IsEnabled(user, featureKey);
}
