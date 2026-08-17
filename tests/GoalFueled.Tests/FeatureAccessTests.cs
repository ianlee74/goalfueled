using GoalFueled.Api.Common;
using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;

namespace GoalFueled.Tests;

/// <summary>
/// Feature-tag resolution: subscription tier ∪ individual grants − individual denials, intersected with
/// the statuses that are enableable at all. Also covers the legacy (null-field) reads that the Cosmos
/// nullable-field rule produces, and the <see cref="FeatureGuard"/> enforcement helper.
/// </summary>
public class FeatureAccessTests
{
    // A free-tier feature (Beta) and a paid-tier one (Beta, Plus+) — the catalog's two enforced gates.
    private const string FreeFeature = FeatureKeys.BarcodeScanning;
    private const string PaidFeature = FeatureKeys.NutritionLabelOcr;
    private const string PlannedFeature = FeatureKeys.AiVoiceLogging;

    private static AppUser NewUser(SubscriptionLevel? level = null) =>
        new() { Id = "u1", Email = "u@dev.local", DisplayName = "U", SubscriptionLevel = level };

    // ---- Subscription tier ----

    [Fact]
    public void Free_tier_includes_the_free_features_but_not_the_paid_ones()
    {
        var user = NewUser(SubscriptionLevel.Free);

        Assert.True(FeatureAccess.IsEnabled(user, FreeFeature));
        Assert.True(FeatureAccess.IsEnabled(user, FeatureKeys.MealPlans));
        Assert.False(FeatureAccess.IsEnabled(user, PaidFeature));
    }

    [Fact]
    public void Paid_tier_includes_its_own_features_and_inherits_the_lower_tiers()
    {
        var plus = NewUser(SubscriptionLevel.Plus);

        Assert.True(FeatureAccess.IsEnabled(plus, PaidFeature));
        Assert.True(FeatureAccess.IsEnabled(plus, FreeFeature));
    }

    [Fact]
    public void Higher_tiers_inherit_everything_a_lower_tier_includes()
    {
        var plus = FeatureAccess.ResolveKeys(NewUser(SubscriptionLevel.Plus));
        var pro = FeatureAccess.ResolveKeys(NewUser(SubscriptionLevel.Pro));

        Assert.All(plus, key => Assert.Contains(key, pro));
    }

    // ---- Legacy documents (the Cosmos nullable-field rule) ----

    [Fact]
    public void A_user_with_no_stored_tier_or_override_lists_resolves_as_free_with_no_overrides()
    {
        // Exactly what a user document written before feature tags existed materializes as.
        var legacy = new AppUser { Id = "legacy", SubscriptionLevel = null, GrantedFeatureKeys = null, DeniedFeatureKeys = null };

        Assert.Equal(SubscriptionLevel.Free, legacy.EffectiveSubscriptionLevel);
        Assert.Equal(FeatureAccessMode.Inherit, FeatureAccess.GetOverride(legacy, FreeFeature));
        Assert.Equal(
            FeatureAccess.ResolveKeys(NewUser(SubscriptionLevel.Free)),
            FeatureAccess.ResolveKeys(legacy));
    }

    // ---- Individual grants and denials ----

    [Fact]
    public void An_individual_grant_adds_a_feature_the_tier_does_not_include()
    {
        var user = NewUser(SubscriptionLevel.Free);
        Assert.False(FeatureAccess.IsEnabled(user, PaidFeature));

        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Granted);

        Assert.True(FeatureAccess.IsEnabled(user, PaidFeature));
        Assert.Equal(FeatureAccessMode.Granted, FeatureAccess.GetOverride(user, PaidFeature));
    }

    [Fact]
    public void An_individual_denial_removes_a_feature_the_tier_does_include()
    {
        var user = NewUser(SubscriptionLevel.Pro);
        Assert.True(FeatureAccess.IsEnabled(user, FreeFeature));

        FeatureAccess.SetOverride(user, FreeFeature, FeatureAccessMode.Denied);

        Assert.False(FeatureAccess.IsEnabled(user, FreeFeature));
        Assert.Equal(FeatureAccessMode.Denied, FeatureAccess.GetOverride(user, FreeFeature));
    }

    [Fact]
    public void A_denial_wins_over_a_grant_for_the_same_feature()
    {
        var user = NewUser(SubscriptionLevel.Free);
        // Force the pathological state directly (SetOverride keeps them mutually exclusive).
        user.GrantedFeatureKeys = new List<string> { PaidFeature };
        user.DeniedFeatureKeys = new List<string> { PaidFeature };

        Assert.False(FeatureAccess.IsEnabled(user, PaidFeature));
    }

    [Fact]
    public void Granting_then_denying_replaces_the_override_rather_than_stacking()
    {
        var user = NewUser(SubscriptionLevel.Free);

        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Granted);
        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Denied);

        Assert.Null(user.GrantedFeatureKeys);
        Assert.Equal(new[] { PaidFeature }, user.DeniedFeatureKeys);
    }

    [Fact]
    public void Inherit_clears_an_override_and_collapses_the_emptied_list_back_to_null()
    {
        var user = NewUser(SubscriptionLevel.Free);
        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Granted);
        Assert.NotNull(user.GrantedFeatureKeys);

        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Inherit);

        // Null (not an empty list) so accounts with no overrides never grow the property in Cosmos.
        Assert.Null(user.GrantedFeatureKeys);
        Assert.Null(user.DeniedFeatureKeys);
        Assert.Equal(FeatureAccessMode.Inherit, FeatureAccess.GetOverride(user, PaidFeature));
    }

    [Fact]
    public void Clearing_one_override_keeps_the_others_in_the_list()
    {
        var user = NewUser(SubscriptionLevel.Free);
        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Granted);
        FeatureAccess.SetOverride(user, FeatureKeys.AccountSharing, FeatureAccessMode.Granted);

        FeatureAccess.SetOverride(user, PaidFeature, FeatureAccessMode.Inherit);

        Assert.Equal(new[] { FeatureKeys.AccountSharing }, user.GrantedFeatureKeys);
    }

    [Fact]
    public void Stored_keys_are_matched_case_insensitively()
    {
        var user = NewUser(SubscriptionLevel.Pro);
        user.DeniedFeatureKeys = new List<string> { FreeFeature.ToUpperInvariant() };

        Assert.False(FeatureAccess.IsEnabled(user, FreeFeature));
        Assert.Equal(FeatureAccessMode.Denied, FeatureAccess.GetOverride(user, FreeFeature));
    }

    // ---- Status participates in resolution ----

    [Fact]
    public void A_planned_feature_is_off_for_everyone_even_at_the_top_tier()
    {
        Assert.False(FeatureAccess.IsEnabled(NewUser(SubscriptionLevel.Pro), PlannedFeature));
    }

    [Fact]
    public void An_individual_grant_cannot_resurrect_a_planned_or_disabled_feature()
    {
        var user = NewUser(SubscriptionLevel.Pro);
        FeatureAccess.SetOverride(user, PlannedFeature, FeatureAccessMode.Granted);

        Assert.False(FeatureAccess.IsEnabled(user, PlannedFeature));
    }

    [Theory]
    // Planned isn't built yet and Disabled is the kill switch — neither can be on for anyone.
    [InlineData(FeatureStatus.Planned, false, false)]
    // Alpha is dogfood-only: never handed out by a tier, only by an explicit per-user grant.
    [InlineData(FeatureStatus.Alpha, true, false)]
    [InlineData(FeatureStatus.Beta, true, true)]
    [InlineData(FeatureStatus.Released, true, true)]
    // Deprecated still behaves like Released so nobody loses access mid-flight; it is only badged.
    [InlineData(FeatureStatus.Deprecated, true, true)]
    [InlineData(FeatureStatus.Disabled, false, false)]
    public void Status_decides_whether_a_feature_is_enableable_and_tier_included(
        FeatureStatus status, bool enableable, bool includedBySubscription)
    {
        var definition = new FeatureDefinition("x", "X", "…", status);

        Assert.Equal(enableable, definition.IsEnableable);
        Assert.Equal(includedBySubscription, definition.IsIncludedBySubscription);
        // A feature no tier hands out reports no included tiers at all.
        Assert.Equal(enableable && includedBySubscription, definition.IncludedLevels.Count > 0);
    }

    [Fact]
    public void Included_levels_expand_up_the_ladder_from_the_minimum()
    {
        var plusFeature = new FeatureDefinition("x", "X", "…", FeatureStatus.Released, SubscriptionLevel.Plus);

        Assert.Equal(new[] { SubscriptionLevel.Plus, SubscriptionLevel.Pro }, plusFeature.IncludedLevels);
    }

    // ---- Catalog lookups ----

    [Fact]
    public void Unknown_feature_keys_are_never_enabled()
    {
        var user = NewUser(SubscriptionLevel.Pro);

        Assert.False(FeatureAccess.IsEnabled(user, "not-a-feature"));
        Assert.False(FeatureAccess.IsEnabled(user, ""));
        Assert.False(FeatureCatalog.IsKnown("not-a-feature"));
    }

    [Fact]
    public void Catalog_keys_are_unique_and_normalize_case_insensitively()
    {
        Assert.Equal(FeatureCatalog.All.Count, FeatureCatalog.All.Select(f => f.Key).Distinct().Count());
        Assert.Equal(FreeFeature, FeatureCatalog.Normalize(FreeFeature.ToUpperInvariant()));
        Assert.Null(FeatureCatalog.Normalize("nope"));
    }

    [Fact]
    public void Setting_an_override_for_an_unknown_key_throws()
    {
        var user = NewUser();

        Assert.Throws<ArgumentException>(() =>
            FeatureAccess.SetOverride(user, "not-a-feature", FeatureAccessMode.Granted));
    }

    // ---- Enforcement (FeatureGuard) ----

    [Fact]
    public void Guard_allows_a_feature_the_user_has()
    {
        var user = NewUser(SubscriptionLevel.Free);

        FeatureGuard.EnsureFeature(user, FreeFeature); // does not throw
        Assert.True(FeatureGuard.Has(user, FreeFeature));
    }

    [Fact]
    public void Guard_throws_forbidden_for_a_feature_the_tier_does_not_include()
    {
        var user = NewUser(SubscriptionLevel.Free);

        var ex = Assert.Throws<ForbiddenException>(() => FeatureGuard.EnsureFeature(user, PaidFeature));
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("Nutrition label OCR", ex.Message);
    }

    [Fact]
    public void Guard_throws_forbidden_for_a_feature_the_user_was_individually_denied()
    {
        var user = NewUser(SubscriptionLevel.Pro);
        FeatureAccess.SetOverride(user, FreeFeature, FeatureAccessMode.Denied);

        Assert.Throws<ForbiddenException>(() => FeatureGuard.EnsureFeature(user, FreeFeature));
    }

    [Fact]
    public void Guard_fails_closed_on_an_unknown_key()
    {
        Assert.Throws<ForbiddenException>(() =>
            FeatureGuard.EnsureFeature(NewUser(SubscriptionLevel.Pro), "not-a-feature"));
    }
}
