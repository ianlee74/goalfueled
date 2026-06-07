using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

public class IngredientServiceTests
{
    // Provisions an admin (first) and a regular user against the same context.
    private static async Task<(AppUser admin, AppUser user, IngredientService svc)> SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var svc = new IngredientService(new IngredientRepository(db));
        return (admin!, user!, svc);
    }

    private static IngredientInput Input(string name, bool appLevel = false, params (string id, double v)[] nutrients) =>
        new(name, null, null, UnitOfMeasure.Gram, 100,
            nutrients.Select(n => new NutrientAmount(n.id, n.v)).ToList(), appLevel);

    [Fact]
    public async Task User_creates_private_ingredient_owned_by_them()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        var created = await svc.CreateAsync(user, Input("Oats", false, ("calories", 389), ("protein", 16.9)), default);

        Assert.Equal(OwnershipScope.User, created.Scope);
        Assert.Equal(user.Id, created.OwnerUserId);
        Assert.True(created.CanEdit);
        Assert.Equal(389, created.NutritionPerServing.Single(n => n.NutrientId == "calories").Value);
    }

    [Fact]
    public async Task Admin_creates_app_level_ingredient_visible_to_everyone()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        await svc.CreateAsync(admin, Input("Salt", appLevel: true), default);

        var asUser = await svc.ListAsync(user, null, default);
        var salt = Assert.Single(asUser, i => i.Name == "Salt");
        Assert.Equal(OwnershipScope.App, salt.Scope);
        Assert.False(salt.CanEdit); // regular user cannot edit app-level items
    }

    [Fact]
    public async Task AppLevel_flag_is_ignored_for_non_admins()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        var created = await svc.CreateAsync(user, Input("Pretend App", appLevel: true), default);

        Assert.Equal(OwnershipScope.User, created.Scope); // forced to user scope
        Assert.Equal(user.Id, created.OwnerUserId);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_private_ingredients()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var mine = await svc.CreateAsync(user, Input("Secret Sauce"), default);

        var adminList = await svc.ListAsync(admin, null, default);
        Assert.DoesNotContain(adminList, i => i.Id == mine.Id);
    }

    [Fact]
    public async Task Owner_can_update_but_other_user_cannot()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await svc.CreateAsync(user, Input("Mine"), default);

        var updated = await svc.UpdateAsync(user, mine.Id, Input("Mine v2"), default);
        Assert.Equal("Mine v2", updated.Name);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.UpdateAsync(other!, mine.Id, Input("Hijacked"), default));
    }

    [Fact]
    public async Task Admin_can_edit_another_users_ingredient()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var mine = await svc.CreateAsync(user, Input("User Item"), default);

        var updated = await svc.UpdateAsync(admin, mine.Id, Input("Edited by admin"), default);
        Assert.Equal("Edited by admin", updated.Name);
    }

    [Fact]
    public async Task Admin_all_flag_returns_other_users_private_ingredients()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var mine = await svc.CreateAsync(user, Input("Secret Sauce"), default);

        // Default (scoped) admin list does not include another user's private item...
        var scoped = await svc.ListAsync(admin, null, all: false, default);
        Assert.DoesNotContain(scoped, i => i.Id == mine.Id);

        // ...but the admin "view all" flag surfaces it.
        var all = await svc.ListAsync(admin, null, all: true, default);
        Assert.Contains(all, i => i.Id == mine.Id);
    }

    [Fact]
    public async Task All_flag_is_ignored_for_non_admins()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        // Admin owns a private item the user must never see, even when passing all=true.
        var adminPrivate = await svc.CreateAsync(admin, Input("Admin Private"), default);

        var asUser = await svc.ListAsync(user, null, all: true, default);
        Assert.DoesNotContain(asUser, i => i.Id == adminPrivate.Id);
    }

    [Fact]
    public async Task All_flag_is_ignored_when_admin_view_is_off()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var mine = await svc.CreateAsync(user, Input("Secret Sauce"), default);

        // Admin with the admin view turned off behaves like a regular user; all=true is a no-op.
        admin.AdminViewEnabled = false;
        var list = await svc.ListAsync(admin, null, all: true, default);
        Assert.DoesNotContain(list, i => i.Id == mine.Id);
    }

    [Fact]
    public async Task Validation_rejects_empty_name_and_unknown_nutrient()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(user, Input(""), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(user, Input("Bad", false, ("not_a_nutrient", 1)), default));
    }

    [Fact]
    public async Task Search_filters_by_name_case_insensitively()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        await svc.CreateAsync(user, Input("Brown Rice"), default);
        await svc.CreateAsync(user, Input("Quinoa"), default);

        var results = await svc.ListAsync(user, "rice", default);
        Assert.Single(results);
        Assert.Equal("Brown Rice", results[0].Name);
    }
}
