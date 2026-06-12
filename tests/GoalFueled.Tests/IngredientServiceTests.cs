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
    public async Task Validation_rejects_oversized_name_description_and_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        // Name over 200 chars.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(user, Input(new string('x', 201)), default));

        // Description over 10,000 chars.
        var longDesc = new IngredientInput(
            "Ok", new string('d', 10_001), null, UnitOfMeasure.Gram, 100, null);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(user, longDesc, default));

        // More nutrition entries than allowed.
        var manyNutrients = Enumerable.Range(0, 201)
            .Select(_ => new NutrientAmount("calories", 1)).ToList();
        var tooMany = new IngredientInput("Ok", null, null, UnitOfMeasure.Gram, 100, manyNutrients);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(user, tooMany, default));
    }

    [Fact]
    public async Task Description_is_sanitized_on_save()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        var input = new IngredientInput(
            "Dangerous", "<p>Hello</p><script>alert('xss')</script>", null,
            UnitOfMeasure.Gram, 100, null);
        var created = await svc.CreateAsync(user, input, default);

        Assert.DoesNotContain("<script", created.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", created.Description);
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

    [Fact]
    public async Task Search_matches_aliases_tags_and_localized_names()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        var chickpeas = new IngredientInput(
            "Chickpeas", null, null, UnitOfMeasure.Gram, 100, null,
            Tags: new[] { "legume", "protein" },
            Aliases: new[] { "Garbanzo Beans" },
            LocalizedNames: new[] { new LocalizedName("es", "garbanzos"), new LocalizedName("fr", "pois chiches") });
        await svc.CreateAsync(user, chickpeas, default);
        await svc.CreateAsync(user, Input("Quinoa"), default);

        Assert.Equal("Chickpeas", Assert.Single(await svc.ListAsync(user, "GARBANZO", default)).Name); // alias
        Assert.Equal("Chickpeas", Assert.Single(await svc.ListAsync(user, "legume", default)).Name);   // tag
        Assert.Equal("Chickpeas", Assert.Single(await svc.ListAsync(user, "pois chiches", default)).Name); // non-English name
    }

    [Fact]
    public async Task Search_is_accent_insensitive_in_both_directions()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        await svc.CreateAsync(user, Input("Jalapeño Pepper"), default);
        await svc.CreateAsync(user, Input("Quinoa"), default);

        Assert.Single(await svc.ListAsync(user, "jalapeno", default)); // plain query matches accented name
        Assert.Single(await svc.ListAsync(user, "jalapeño", default)); // accented query still matches
    }

    [Fact]
    public async Task Search_metadata_is_normalized_on_save()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        var input = new IngredientInput(
            "Chickpeas", null, null, UnitOfMeasure.Gram, 100, null,
            Tags: new[] { " Protein ", "protein", "" },
            Aliases: new[] { "   ", "Garbanzo Beans " },
            LocalizedNames: new[]
            {
                new LocalizedName(" ES ", " Garbanzos "),
                new LocalizedName("es", "garbanzos"), // duplicate (language, name) pair
                new LocalizedName("", "orphan"),      // missing language → dropped
            });
        var created = await svc.CreateAsync(user, input, default);

        Assert.Equal(new[] { "Protein" }, created.Tags);
        Assert.Equal(new[] { "Garbanzo Beans" }, created.Aliases);
        var localized = Assert.Single(created.LocalizedNames);
        Assert.Equal("es", localized.Language);
        Assert.Equal("Garbanzos", localized.Name);
    }
}
