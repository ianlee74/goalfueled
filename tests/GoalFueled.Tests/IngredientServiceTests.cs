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

    // ---- customize (clone) ----

    [Fact]
    public async Task Customize_creates_a_private_copy_owned_by_the_caller()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var appItem = await svc.CreateAsync(admin, Input("Olive Oil", appLevel: true, ("calories", 884)), default);

        var clone = await svc.CustomizeAsync(user, appItem.Id, default);

        Assert.NotEqual(appItem.Id, clone.Id);          // fresh id
        Assert.Equal(OwnershipScope.User, clone.Scope); // private
        Assert.Equal(user.Id, clone.OwnerUserId);       // owned by the caller
        Assert.True(clone.CanEdit);                     // the caller may now edit it
        Assert.False(clone.IsFavorite);                 // favorite state is not copied
        Assert.Equal("Olive Oil (custom)", clone.Name);
    }

    [Fact]
    public async Task Customize_deep_copies_all_fields_and_leaves_the_source_unchanged()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);

        var source = await svc.CreateAsync(admin, new IngredientInput(
            "Chickpeas", "<p>Tasty</p>", "https://img/chickpeas.png", UnitOfMeasure.Gram, 100,
            new[] { new NutrientAmount("calories", 364), new NutrientAmount("protein", 19) },
            AppLevel: true,
            Tags: new[] { "legume", "protein" },
            Aliases: new[] { "Garbanzo Beans" },
            LocalizedNames: new[] { new LocalizedName("es", "garbanzos") }), default);

        var clone = await svc.CustomizeAsync(user, source.Id, default);

        Assert.Equal(source.Description, clone.Description);
        Assert.Equal(source.ImageUrl, clone.ImageUrl);
        Assert.Equal(source.Unit, clone.Unit);
        Assert.Equal(source.ServingSize, clone.ServingSize);
        Assert.Equal(364, clone.NutritionPerServing.Single(n => n.NutrientId == "calories").Value);
        Assert.Equal(19, clone.NutritionPerServing.Single(n => n.NutrientId == "protein").Value);
        Assert.Equal(new[] { "legume", "protein" }, clone.Tags);
        Assert.Equal(new[] { "Garbanzo Beans" }, clone.Aliases);
        var localized = Assert.Single(clone.LocalizedNames);
        Assert.Equal("es", localized.Language);
        Assert.Equal("garbanzos", localized.Name);

        // The source app-level ingredient is untouched.
        var reloaded = await svc.GetAsync(admin, source.Id, default);
        Assert.Equal(OwnershipScope.App, reloaded.Scope);
        Assert.Equal("Chickpeas", reloaded.Name);
    }

    [Fact]
    public async Task Customize_rejects_an_ingredient_the_user_cannot_see()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var theirs = await svc.CreateAsync(other!, Input("Their Secret"), default);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.CustomizeAsync(user, theirs.Id, default));
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

    // ---- review workflow state transitions ----

    [Fact]
    public async Task User_creates_private_ingredient_credited_to_them()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);

        var created = await svc.CreateAsync(user, Input("Oats"), default);

        Assert.Equal(IngredientVisibility.Private, created.Visibility);
        Assert.Equal(user.Id, created.OriginalCreatorUserId);
    }

    [Fact]
    public async Task Admin_app_level_ingredient_is_public()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, svc) = await SetupAsync(db);

        var created = await svc.CreateAsync(admin, Input("Salt", appLevel: true), default);

        Assert.Equal(IngredientVisibility.Public, created.Visibility);
    }

    [Fact]
    public async Task Submit_for_review_moves_private_to_pending()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Public Candidate"), default);

        var pending = await svc.SubmitForReviewAsync(user, mine.Id, default);

        Assert.Equal(IngredientVisibility.PendingReview, pending.Visibility);
    }

    [Fact]
    public async Task Only_owner_can_submit_for_review()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Mine"), default);

        // Even an admin is not the owner — submission is an owner-only action.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SubmitForReviewAsync(admin, mine.Id, default));
    }

    [Fact]
    public async Task Admin_approves_pending_ingredient_to_public_keeping_creator_credit()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Candidate"), default);
        await svc.SubmitForReviewAsync(user, mine.Id, default);

        var queue = await svc.ListReviewQueueAsync(admin, default);
        Assert.Contains(queue, i => i.Id == mine.Id);

        var approved = await svc.ApproveAsync(admin, mine.Id, default);

        Assert.Equal(IngredientVisibility.Public, approved.Visibility);
        Assert.Equal(user.Id, approved.OriginalCreatorUserId);
        // Approval keeps the ingredient user-scoped/owned (it does not become an app-level ingredient).
        Assert.Equal(OwnershipScope.User, approved.Scope);
        Assert.Equal(user.Id, approved.OwnerUserId);
    }

    [Fact]
    public async Task Public_ingredient_can_never_become_private_again()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Candidate"), default);
        await svc.SubmitForReviewAsync(user, mine.Id, default);
        await svc.ApproveAsync(admin, mine.Id, default);

        // Owner editing the ingredient does not change its (now public) visibility.
        var edited = await svc.UpdateAsync(user, mine.Id, Input("Candidate v2"), default);
        Assert.Equal(IngredientVisibility.Public, edited.Visibility);

        // Resubmitting a public ingredient is rejected.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitForReviewAsync(user, mine.Id, default));
        // It is no longer eligible for submission per the DTO flag either.
        Assert.False(edited.CanSubmitForReview);
    }

    [Fact]
    public async Task Admin_rejects_pending_ingredient_and_owner_can_resubmit()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Candidate"), default);
        await svc.SubmitForReviewAsync(user, mine.Id, default);

        var rejected = await svc.RejectAsync(admin, mine.Id, default);
        Assert.Equal(IngredientVisibility.Rejected, rejected.Visibility);

        // A rejected ingredient may be revised and resubmitted.
        var resubmitted = await svc.SubmitForReviewAsync(user, mine.Id, default);
        Assert.Equal(IngredientVisibility.PendingReview, resubmitted.Visibility);
    }

    [Fact]
    public async Task Non_admin_cannot_access_review_queue_or_approve()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Candidate"), default);
        await svc.SubmitForReviewAsync(user, mine.Id, default);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListReviewQueueAsync(user, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ApproveAsync(user, mine.Id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.RejectAsync(user, mine.Id, default));
    }

    [Fact]
    public async Task Approve_only_valid_from_pending_state()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var mine = await svc.CreateAsync(user, Input("Still Private"), default);

        // Not yet submitted → cannot approve.
        await Assert.ThrowsAsync<ValidationException>(() => svc.ApproveAsync(admin, mine.Id, default));
    }

    [Fact]
    public async Task Approved_public_ingredient_visible_to_other_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, svc) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await svc.CreateAsync(user, Input("Shared"), default);
        await svc.SubmitForReviewAsync(user, mine.Id, default);
        await svc.ApproveAsync(admin, mine.Id, default);

        var otherList = await svc.ListAsync(other!, null, default);
        Assert.Contains(otherList, i => i.Id == mine.Id);
        // But the other user cannot edit it.
        var seen = await svc.GetAsync(other!, mine.Id, default);
        Assert.False(seen.CanEdit);
    }
}
