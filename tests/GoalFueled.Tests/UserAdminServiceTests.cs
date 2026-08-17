using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;

namespace GoalFueled.Tests;

public class UserAdminServiceTests
{
    // Provisions an admin (first user) and a regular user against the same context, plus the service.
    private static async Task<(AppUser admin, AppUser user, UserRepository repo, UserAdminService svc)>
        SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var repo = new UserRepository(db);
        return (admin!, user!, repo, NewService(db, repo));
    }

    // Builds the service wired to every repository over the same context (the cascade needs them all).
    private static UserAdminService NewService(GoalFueledDbContext db, UserRepository repo) => new(
        repo,
        new IngredientRepository(db),
        new RecipeRepository(db),
        new MealRepository(db),
        new MealPlanRepository(db),
        new ConsumptionEntryRepository(db),
        new BurnActivityRepository(db),
        new WeightEntryRepository(db),
        new GoalRepository(db),
        new UserStatsRepository(db),
        new ShareInvitationRepository(db));

    [Fact]
    public async Task Acting_admin_lists_all_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        var users = await svc.ListAsync(admin, default);

        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.Id == admin.Id && u.IsAdmin && u.IsSelf);
        Assert.Contains(users, u => u.Id == user.Id && !u.IsAdmin && !u.IsSelf);
    }

    [Fact]
    public async Task Non_admin_cannot_list_users()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListAsync(user, default));
    }

    [Fact]
    public async Task Admin_with_view_disabled_cannot_list_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        // Turning the admin view off means the user is no longer acting as an admin.
        admin.AdminViewEnabled = false;

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListAsync(admin, default));
    }

    [Fact]
    public async Task Admin_can_promote_and_demote_another_user()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        var promoted = await svc.SetAdminRoleAsync(admin, user.Id, true, default);
        Assert.True(promoted.IsAdmin);
        Assert.Contains(AppRole.AppAdmin, (await repo.GetByIdAsync(user.Id))!.Roles);

        var demoted = await svc.SetAdminRoleAsync(admin, user.Id, false, default);
        Assert.False(demoted.IsAdmin);
        Assert.DoesNotContain(AppRole.AppAdmin, (await repo.GetByIdAsync(user.Id))!.Roles);
    }

    [Fact]
    public async Task Cannot_demote_the_last_remaining_admin()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        // The first user is the only admin; demoting them would lock everyone out.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetAdminRoleAsync(admin, admin.Id, false, default));
    }

    [Fact]
    public async Task Admin_can_demote_self_when_another_admin_exists()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        // Promote the second user so two admins exist, then the first may step down.
        await svc.SetAdminRoleAsync(admin, user.Id, true, default);
        var demoted = await svc.SetAdminRoleAsync(admin, admin.Id, false, default);

        Assert.False(demoted.IsAdmin);
        Assert.Equal(1, await repo.CountAdminsAsync());
    }

    [Fact]
    public async Task Non_admin_cannot_change_roles()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetAdminRoleAsync(user, admin.Id, false, default));
    }

    [Fact]
    public async Task Delete_removes_all_of_the_users_personal_data_and_the_user_doc()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        // Seed a spread of the user's personal/private data.
        var privateIngredient = new Ingredient { Name = "Private", Scope = OwnershipScope.User, OwnerUserId = user.Id };
        var pendingRecipe = new Recipe { Name = "Pending", Scope = OwnershipScope.User, OwnerUserId = user.Id, Visibility = RecipeVisibility.PendingReview };
        var privateMeal = new Meal { Name = "Private meal", Scope = OwnershipScope.User, OwnerUserId = user.Id };
        await new IngredientRepository(db).AddAsync(privateIngredient);
        await new RecipeRepository(db).AddAsync(pendingRecipe);
        await new MealRepository(db).AddAsync(privateMeal);
        await new MealPlanRepository(db).AddAsync(new MealPlan { Name = "Plan", OwnerUserId = user.Id });
        await new ConsumptionEntryRepository(db).AddAsync(new ConsumptionEntry { OwnerUserId = user.Id, Name = "Lunch" });
        await new BurnActivityRepository(db).AddAsync(new BurnActivity { OwnerUserId = user.Id, Name = "Run", CaloriesBurned = 300 });
        await new WeightEntryRepository(db).AddAsync(new WeightEntry { OwnerUserId = user.Id, Value = 180 });
        await new GoalRepository(db).AddAsync(new Goal { OwnerUserId = user.Id, Name = "Cut" });
        await new UserStatsRepository(db).AddAsync(new UserStats { OwnerUserId = user.Id, FuelPoints = 50 });

        await svc.DeleteAsync(admin, user.Id, default);

        Assert.Null(await repo.GetByIdAsync(user.Id));
        Assert.Empty(await new IngredientRepository(db).ListByOwnerAsync(user.Id));
        Assert.Empty(await new RecipeRepository(db).ListByOwnerAsync(user.Id));
        Assert.Empty(await new MealRepository(db).ListByOwnerAsync(user.Id));
        Assert.Empty(await new MealPlanRepository(db).ListByOwnerAsync(user.Id));
        Assert.Empty(await new ConsumptionEntryRepository(db).ListForUserAsync(user.Id));
        Assert.Empty(await new BurnActivityRepository(db).ListForUserAsync(user.Id));
        Assert.Empty(await new WeightEntryRepository(db).ListForUserAsync(user.Id));
        Assert.Empty(await new GoalRepository(db).ListForUserAsync(user.Id));
        Assert.Null(await new UserStatsRepository(db).GetForUserAsync(user.Id));
    }

    [Fact]
    public async Task Delete_preserves_and_rehomes_public_ingredients_and_recipes_to_the_app()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        var publicIngredient = new Ingredient
        {
            Name = "Approved ingredient", Scope = OwnershipScope.User, OwnerUserId = user.Id,
            Visibility = IngredientVisibility.Public,
        };
        var publicRecipe = new Recipe
        {
            Name = "Approved recipe", Scope = OwnershipScope.User, OwnerUserId = user.Id,
            Visibility = RecipeVisibility.Public,
        };
        await new IngredientRepository(db).AddAsync(publicIngredient);
        await new RecipeRepository(db).AddAsync(publicRecipe);

        await svc.DeleteAsync(admin, user.Id, default);

        var keptIngredient = await new IngredientRepository(db).GetByIdAsync(publicIngredient.Id);
        Assert.NotNull(keptIngredient);
        Assert.Equal(OwnershipScope.App, keptIngredient!.Scope);
        Assert.Null(keptIngredient.OwnerUserId);
        Assert.Equal(user.Id, keptIngredient.OriginalCreatorUserId);
        Assert.Equal(IngredientVisibility.Public, keptIngredient.EffectiveVisibility);

        var keptRecipe = await new RecipeRepository(db).GetByIdAsync(publicRecipe.Id);
        Assert.NotNull(keptRecipe);
        Assert.Equal(OwnershipScope.App, keptRecipe!.Scope);
        Assert.Null(keptRecipe.OwnerUserId);
        Assert.Equal(user.Id, keptRecipe.OriginalCreatorUserId);
        Assert.Equal(RecipeVisibility.Public, keptRecipe.Visibility);
    }

    [Fact]
    public async Task Delete_keeps_existing_original_creator_credit_when_rehoming()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        var ingredient = new Ingredient
        {
            Name = "Approved", Scope = OwnershipScope.User, OwnerUserId = user.Id,
            Visibility = IngredientVisibility.Public, OriginalCreatorUserId = "someone-else",
        };
        await new IngredientRepository(db).AddAsync(ingredient);

        await svc.DeleteAsync(admin, user.Id, default);

        var kept = await new IngredientRepository(db).GetByIdAsync(ingredient.Id);
        Assert.Equal("someone-else", kept!.OriginalCreatorUserId);
    }

    [Fact]
    public async Task Cannot_delete_self()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(admin, admin.Id, default));
    }

    [Fact]
    public async Task Cannot_delete_the_last_remaining_admin()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        // 'admin' is the only admin in the DB. A separate (non-persisted) acting-admin actor attempts to
        // delete them — passes the authorization and self checks, but the last-admin guard must block it.
        var actor = new AppUser { Id = "other-actor", Roles = { AppRole.AppAdmin }, AdminViewEnabled = true };
        await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(actor, admin.Id, default));
    }

    [Fact]
    public async Task Delete_missing_user_returns_not_found()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteAsync(admin, "does-not-exist", default));
    }

    [Fact]
    public async Task Non_admin_cannot_delete_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(user, admin.Id, default));
    }

    // ---- Feature tags: catalog, subscription tier, and per-user grants/denials ----

    [Fact]
    public async Task Acting_admin_sees_the_whole_feature_catalog()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        var catalog = svc.ListFeatures(admin);

        Assert.Equal(FeatureCatalog.All.Count, catalog.Count);
        var ocr = Assert.Single(catalog, f => f.Key == FeatureKeys.NutritionLabelOcr);
        Assert.Equal(FeatureStatus.Beta, ocr.Status);
        Assert.Equal(SubscriptionLevel.Plus, ocr.MinimumSubscriptionLevel);
        Assert.Equal(new[] { SubscriptionLevel.Plus, SubscriptionLevel.Pro }, ocr.IncludedLevels);
    }

    [Fact]
    public async Task Non_admin_cannot_see_the_feature_catalog()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, svc) = await SetupAsync(db);

        Assert.Throws<ForbiddenException>(() => svc.ListFeatures(user));
    }

    [Fact]
    public async Task New_users_start_on_the_free_tier_with_no_overrides()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        var dto = Assert.Single(await svc.ListAsync(admin, default), u => u.Id == user.Id);

        Assert.Equal(SubscriptionLevel.Free, dto.SubscriptionLevel);
        Assert.Empty(dto.GrantedFeatureKeys!);
        Assert.Empty(dto.DeniedFeatureKeys!);
        Assert.Contains(FeatureKeys.BarcodeScanning, dto.EffectiveFeatureKeys!);
        Assert.DoesNotContain(FeatureKeys.NutritionLabelOcr, dto.EffectiveFeatureKeys!);
    }

    [Fact]
    public async Task Admin_can_move_a_user_to_a_paid_tier_which_widens_their_effective_features()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        var updated = await svc.SetSubscriptionLevelAsync(admin, user.Id, SubscriptionLevel.Plus, default);

        Assert.Equal(SubscriptionLevel.Plus, updated.SubscriptionLevel);
        Assert.Contains(FeatureKeys.NutritionLabelOcr, updated.EffectiveFeatureKeys!);
        Assert.Equal(SubscriptionLevel.Plus, (await repo.GetByIdAsync(user.Id))!.SubscriptionLevel);
    }

    [Fact]
    public async Task Admin_can_grant_a_paid_feature_to_a_free_user_individually()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        var updated = await svc.SetFeatureAccessAsync(admin, user.Id,
            new SetFeatureAccessInput(FeatureKeys.NutritionLabelOcr, FeatureAccessMode.Granted), default);

        Assert.Equal(SubscriptionLevel.Free, updated.SubscriptionLevel);
        Assert.Contains(FeatureKeys.NutritionLabelOcr, updated.GrantedFeatureKeys!);
        Assert.Contains(FeatureKeys.NutritionLabelOcr, updated.EffectiveFeatureKeys!);

        var persisted = await repo.GetByIdAsync(user.Id);
        Assert.True(FeatureAccess.IsEnabled(persisted!, FeatureKeys.NutritionLabelOcr));
    }

    [Fact]
    public async Task Admin_can_deny_a_user_a_feature_their_tier_includes()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);
        await svc.SetSubscriptionLevelAsync(admin, user.Id, SubscriptionLevel.Pro, default);

        var updated = await svc.SetFeatureAccessAsync(admin, user.Id,
            new SetFeatureAccessInput(FeatureKeys.BarcodeScanning, FeatureAccessMode.Denied), default);

        Assert.Contains(FeatureKeys.BarcodeScanning, updated.DeniedFeatureKeys!);
        Assert.DoesNotContain(FeatureKeys.BarcodeScanning, updated.EffectiveFeatureKeys!);
        Assert.False(FeatureAccess.IsEnabled((await repo.GetByIdAsync(user.Id))!, FeatureKeys.BarcodeScanning));
    }

    [Fact]
    public async Task Clearing_an_override_returns_the_user_to_their_tier()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);
        await svc.SetFeatureAccessAsync(admin, user.Id,
            new SetFeatureAccessInput(FeatureKeys.NutritionLabelOcr, FeatureAccessMode.Granted), default);

        var updated = await svc.SetFeatureAccessAsync(admin, user.Id,
            new SetFeatureAccessInput(FeatureKeys.NutritionLabelOcr, FeatureAccessMode.Inherit), default);

        Assert.Empty(updated.GrantedFeatureKeys!);
        Assert.DoesNotContain(FeatureKeys.NutritionLabelOcr, updated.EffectiveFeatureKeys!);
        // The emptied list collapses back to null so the document stays as it was before any override.
        Assert.Null((await repo.GetByIdAsync(user.Id))!.GrantedFeatureKeys);
    }

    [Fact]
    public async Task Non_admin_cannot_change_a_subscription_level_or_feature_grants()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        // Not even for themselves — the tier is never self-serviceable.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SetSubscriptionLevelAsync(user, user.Id, SubscriptionLevel.Pro, default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SetFeatureAccessAsync(user, user.Id,
                new SetFeatureAccessInput(FeatureKeys.NutritionLabelOcr, FeatureAccessMode.Granted), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SetFeatureAccessAsync(user, admin.Id,
                new SetFeatureAccessInput(FeatureKeys.BarcodeScanning, FeatureAccessMode.Denied), default));
    }

    [Fact]
    public async Task Admin_with_view_disabled_cannot_change_feature_access()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);
        admin.AdminViewEnabled = false;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SetSubscriptionLevelAsync(admin, user.Id, SubscriptionLevel.Pro, default));
    }

    [Fact]
    public async Task Unknown_feature_keys_and_levels_are_rejected()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetFeatureAccessAsync(admin, user.Id,
                new SetFeatureAccessInput("not-a-feature", FeatureAccessMode.Granted), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetSubscriptionLevelAsync(admin, user.Id, (SubscriptionLevel)99, default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetFeatureAccessAsync(admin, user.Id,
                new SetFeatureAccessInput(FeatureKeys.BarcodeScanning, (FeatureAccessMode)99), default));
    }

    [Fact]
    public async Task Feature_administration_of_a_missing_user_returns_not_found()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.SetSubscriptionLevelAsync(admin, "does-not-exist", SubscriptionLevel.Plus, default));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.SetFeatureAccessAsync(admin, "does-not-exist",
                new SetFeatureAccessInput(FeatureKeys.BarcodeScanning, FeatureAccessMode.Granted), default));
    }
}
