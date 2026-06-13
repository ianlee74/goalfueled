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

public class RecipeServiceTests
{
    // Provisions an admin (first) and a regular user, plus both services against the same context.
    private static async Task<(AppUser admin, AppUser user, IngredientService ingredients, RecipeService recipes)>
        SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var ingredients = new IngredientService(new IngredientRepository(db));
        var recipes = new RecipeService(new RecipeRepository(db), new IngredientRepository(db));
        return (admin!, user!, ingredients, recipes);
    }

    private static IngredientInput Ingredient(
        string name, UnitOfMeasure unit, double serving, bool appLevel, params (string id, double v)[] nutrients) =>
        new(name, null, null, unit, serving, nutrients.Select(n => new NutrientAmount(n.id, n.v)).ToList(), appLevel);

    private static RecipeInput Recipe(string name, double servings, bool appLevel, params RecipeItemInput[] items) =>
        new(name, null, null, items.ToList(), servings, appLevel);

    private static double Nutrient(IReadOnlyList<NutrientAmount> amounts, string id) =>
        amounts.SingleOrDefault(a => a.NutrientId == id)?.Value ?? 0;

    // ---- RBAC ----

    [Fact]
    public async Task User_creates_private_recipe_owned_and_credited_to_them()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);

        var created = await recipes.CreateAsync(user, Recipe("Lunch", 2, false), default);

        Assert.Equal(OwnershipScope.User, created.Scope);
        Assert.Equal(user.Id, created.OwnerUserId);
        Assert.Equal(user.Id, created.OriginalCreatorUserId);
        Assert.Equal(RecipeVisibility.Private, created.Visibility);
        Assert.True(created.CanEdit);
    }

    [Fact]
    public async Task Admin_creates_app_level_public_recipe_visible_to_everyone()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);

        await recipes.CreateAsync(admin, Recipe("House Salad", 1, appLevel: true), default);

        var asUser = await recipes.ListAsync(user, null, default);
        var salad = Assert.Single(asUser, r => r.Name == "House Salad");
        Assert.Equal(OwnershipScope.App, salad.Scope);
        Assert.Equal(RecipeVisibility.Public, salad.Visibility);
        Assert.False(salad.CanEdit);
    }

    [Fact]
    public async Task AppLevel_flag_is_ignored_for_non_admins()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);

        var created = await recipes.CreateAsync(user, Recipe("Pretend App", 1, appLevel: true), default);

        Assert.Equal(OwnershipScope.User, created.Scope);
        Assert.Equal(RecipeVisibility.Private, created.Visibility);
        Assert.Equal(user.Id, created.OwnerUserId);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_private_recipes()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await recipes.CreateAsync(user, Recipe("Secret", 1, false), default);

        var otherList = await recipes.ListAsync(other!, null, default);
        Assert.DoesNotContain(otherList, r => r.Id == mine.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => recipes.GetAsync(other!, mine.Id, default));
    }

    [Fact]
    public async Task Owner_can_update_but_other_user_cannot()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await recipes.CreateAsync(user, Recipe("Mine", 1, false), default);

        var updated = await recipes.UpdateAsync(user, mine.Id, Recipe("Mine v2", 1, false), default);
        Assert.Equal("Mine v2", updated.Name);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            recipes.UpdateAsync(other!, mine.Id, Recipe("Hijacked", 1, false), default));
    }

    [Fact]
    public async Task Admin_can_edit_another_users_recipe()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);

        var mine = await recipes.CreateAsync(user, Recipe("User Recipe", 1, false), default);

        var updated = await recipes.UpdateAsync(admin, mine.Id, Recipe("Edited by admin", 1, false), default);
        Assert.Equal("Edited by admin", updated.Name);
    }

    [Fact]
    public async Task Validation_rejects_empty_name_zero_servings_and_unusable_ingredient()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => recipes.CreateAsync(user, Recipe("", 1, false), default));
        await Assert.ThrowsAsync<ValidationException>(() => recipes.CreateAsync(user, Recipe("Bad", 0, false), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.CreateAsync(user, Recipe("Ghost", 1, false,
                new RecipeItemInput("does-not-exist", 1, UnitOfMeasure.Gram)), default));
    }

    [Fact]
    public async Task Cannot_reference_another_users_private_ingredient()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, ingredients, recipes) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var theirs = await ingredients.CreateAsync(other!, Ingredient("Theirs", UnitOfMeasure.Gram, 100, false), default);

        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.CreateAsync(user, Recipe("Steal", 1, false,
                new RecipeItemInput(theirs.Id, 50, UnitOfMeasure.Gram)), default));
    }

    [Fact]
    public async Task Incompatible_item_unit_is_rejected_on_save()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);
        // App-level gram-based ingredient; trying to add it by volume should fail validation.
        var flour = await ingredients.CreateAsync(admin, Ingredient("Flour", UnitOfMeasure.Gram, 100, true), default);

        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.CreateAsync(user, Recipe("Mismatch", 1, false,
                new RecipeItemInput(flour.Id, 1, UnitOfMeasure.Cup)), default));
    }

    [Fact]
    public async Task Validation_rejects_oversized_name_description_and_items()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);

        // Name over 200 chars.
        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.CreateAsync(user, Recipe(new string('x', 201), 1, false), default));

        // Description over 10,000 chars.
        var longDesc = new RecipeInput(
            "Ok", new string('d', 10_001), null, new List<RecipeItemInput>(), 1, false);
        await Assert.ThrowsAsync<ValidationException>(() => recipes.CreateAsync(user, longDesc, default));

        // More items than allowed (use a real app-level ingredient so only the count cap trips).
        var flour = await ingredients.CreateAsync(admin, Ingredient("Flour", UnitOfMeasure.Gram, 100, true), default);
        var manyItems = Enumerable.Range(0, 101)
            .Select(_ => new RecipeItemInput(flour.Id, 1, UnitOfMeasure.Gram)).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.CreateAsync(user, Recipe("Too many", 1, false, manyItems), default));
    }

    [Fact]
    public async Task Description_is_sanitized_on_save()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);

        var input = new RecipeInput(
            "Dangerous", "<b>Tasty</b><script>steal()</script>", null,
            new List<RecipeItemInput>(), 1, false);
        var created = await recipes.CreateAsync(user, input, default);

        Assert.DoesNotContain("<script", created.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tasty", created.Description);
    }

    [Fact]
    public async Task Submit_for_review_is_capped_per_user()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);

        // Fill the pending-review queue to the cap (10).
        for (var i = 0; i < 10; i++)
        {
            var r = await recipes.CreateAsync(user, Recipe($"Candidate {i}", 1, false), default);
            await recipes.SubmitForReviewAsync(user, r.Id, default);
        }

        // The 11th submission is rejected.
        var extra = await recipes.CreateAsync(user, Recipe("Over the limit", 1, false), default);
        await Assert.ThrowsAsync<ValidationException>(() =>
            recipes.SubmitForReviewAsync(user, extra.Id, default));
    }

    // ---- customize (clone) ----

    [Fact]
    public async Task Customize_creates_a_private_copy_owned_and_credited_to_the_caller()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);
        var flour = await ingredients.CreateAsync(admin, Ingredient("Flour", UnitOfMeasure.Gram, 100, true), default);

        var appRecipe = await recipes.CreateAsync(admin, Recipe("House Bread", 2, appLevel: true,
            new RecipeItemInput(flour.Id, 250, UnitOfMeasure.Gram)), default);

        var clone = await recipes.CustomizeAsync(user, appRecipe.Id, default);

        Assert.NotEqual(appRecipe.Id, clone.Id);             // fresh id
        Assert.Equal(OwnershipScope.User, clone.Scope);      // private
        Assert.Equal(user.Id, clone.OwnerUserId);            // owned by the caller
        Assert.Equal(user.Id, clone.OriginalCreatorUserId);  // credited to the caller
        Assert.Equal(RecipeVisibility.Private, clone.Visibility);
        Assert.True(clone.CanEdit);
        Assert.False(clone.IsFavorite);                      // favorite state is not copied
        Assert.Equal("House Bread (custom)", clone.Name);
    }

    [Fact]
    public async Task Customize_deep_copies_items_and_servings_and_leaves_source_unchanged()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);
        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165), ("protein", 31)), default);
        var oil = await ingredients.CreateAsync(admin,
            Ingredient("Olive Oil", UnitOfMeasure.Tablespoon, 1, true, ("calories", 119)), default);

        var source = await recipes.CreateAsync(admin, Recipe("Dish", 3, appLevel: true,
            new RecipeItemInput(chicken.Id, 200, UnitOfMeasure.Gram),
            new RecipeItemInput(oil.Id, 0.5, UnitOfMeasure.Tablespoon)), default);

        var clone = await recipes.CustomizeAsync(user, source.Id, default);

        Assert.Equal(3, clone.Servings);
        Assert.Equal(2, clone.Items.Count);
        Assert.Contains(clone.Items, i => i.IngredientId == chicken.Id && i.Quantity == 200 && i.Unit == UnitOfMeasure.Gram);
        Assert.Contains(clone.Items, i => i.IngredientId == oil.Id && i.Quantity == 0.5 && i.Unit == UnitOfMeasure.Tablespoon);
        // Nutrition rolls up identically to the source (same items/servings).
        Assert.Equal(Nutrient(source.TotalNutrition, "calories"), Nutrient(clone.TotalNutrition, "calories"), 3);

        // The source app-level recipe is untouched.
        var reloaded = await recipes.GetAsync(admin, source.Id, default);
        Assert.Equal(OwnershipScope.App, reloaded.Scope);
        Assert.Equal(RecipeVisibility.Public, reloaded.Visibility);
        Assert.Equal("Dish", reloaded.Name);
    }

    [Fact]
    public async Task Customize_rejects_a_recipe_the_user_cannot_see()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var theirs = await recipes.CreateAsync(other!, Recipe("Their Secret", 1, false), default);

        await Assert.ThrowsAsync<NotFoundException>(() => recipes.CustomizeAsync(user, theirs.Id, default));
    }

    // ---- nutrition roll-up math ----

    [Fact]
    public async Task Total_nutrition_scales_by_quantity_and_converts_units()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);

        // 100 g of chicken = 165 kcal, 31 g protein.
        var chicken = await ingredients.CreateAsync(admin,
            Ingredient("Chicken", UnitOfMeasure.Gram, 100, true, ("calories", 165), ("protein", 31)), default);
        // Serving defined per 1 tbsp olive oil = 119 kcal.
        var oil = await ingredients.CreateAsync(admin,
            Ingredient("Olive Oil", UnitOfMeasure.Tablespoon, 1, true, ("calories", 119)), default);

        // 200 g chicken (2 servings) + 0.5 tbsp oil (0.5 servings), recipe yields 2 servings.
        var recipe = await recipes.CreateAsync(user, Recipe("Dish", 2, false,
            new RecipeItemInput(chicken.Id, 200, UnitOfMeasure.Gram),
            new RecipeItemInput(oil.Id, 0.5, UnitOfMeasure.Tablespoon)), default);

        // calories: 165*2 + 119*0.5 = 330 + 59.5 = 389.5
        Assert.Equal(389.5, Nutrient(recipe.TotalNutrition, "calories"), 3);
        // protein: 31*2 = 62
        Assert.Equal(62, Nutrient(recipe.TotalNutrition, "protein"), 3);
        // per serving = total / 2
        Assert.Equal(194.75, Nutrient(recipe.NutritionPerServing, "calories"), 3);
        Assert.Equal(31, Nutrient(recipe.NutritionPerServing, "protein"), 3);
    }

    [Fact]
    public async Task Total_nutrition_converts_across_compatible_units()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, ingredients, recipes) = await SetupAsync(db);

        // 100 g sugar = 400 kcal. Add 1 kg (1000 g) → 10 servings → 4000 kcal.
        var sugar = await ingredients.CreateAsync(admin,
            Ingredient("Sugar", UnitOfMeasure.Gram, 100, true, ("calories", 400)), default);

        var recipe = await recipes.CreateAsync(user, Recipe("Bulk", 1, false,
            new RecipeItemInput(sugar.Id, 1, UnitOfMeasure.Kilogram)), default);

        Assert.Equal(4000, Nutrient(recipe.TotalNutrition, "calories"), 3);
    }

    // ---- review workflow state transitions ----

    [Fact]
    public async Task Submit_for_review_moves_private_to_pending()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Public Candidate", 1, false), default);

        var pending = await recipes.SubmitForReviewAsync(user, mine.Id, default);

        Assert.Equal(RecipeVisibility.PendingReview, pending.Visibility);
    }

    [Fact]
    public async Task Only_owner_can_submit_for_review()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Mine", 1, false), default);

        // Even an admin is not the owner — submission is an owner-only action.
        await Assert.ThrowsAsync<ForbiddenException>(() => recipes.SubmitForReviewAsync(admin, mine.Id, default));
    }

    [Fact]
    public async Task Admin_approves_pending_recipe_to_public_keeping_creator_credit()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Candidate", 1, false), default);
        await recipes.SubmitForReviewAsync(user, mine.Id, default);

        var queue = await recipes.ListReviewQueueAsync(admin, default);
        Assert.Contains(queue, r => r.Id == mine.Id);

        var approved = await recipes.ApproveAsync(admin, mine.Id, default);

        Assert.Equal(RecipeVisibility.Public, approved.Visibility);
        Assert.Equal(user.Id, approved.OriginalCreatorUserId);
    }

    [Fact]
    public async Task Public_recipe_can_never_become_private_again()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Candidate", 1, false), default);
        await recipes.SubmitForReviewAsync(user, mine.Id, default);
        await recipes.ApproveAsync(admin, mine.Id, default);

        // Owner editing the recipe does not change its (now public) visibility.
        var edited = await recipes.UpdateAsync(user, mine.Id, Recipe("Candidate v2", 1, false), default);
        Assert.Equal(RecipeVisibility.Public, edited.Visibility);

        // Resubmitting a public recipe is rejected.
        await Assert.ThrowsAsync<ValidationException>(() => recipes.SubmitForReviewAsync(user, mine.Id, default));
        // It is no longer eligible for submission per the DTO flag either.
        Assert.False(edited.CanSubmitForReview);
    }

    [Fact]
    public async Task Admin_rejects_pending_recipe_and_owner_can_resubmit()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Candidate", 1, false), default);
        await recipes.SubmitForReviewAsync(user, mine.Id, default);

        var rejected = await recipes.RejectAsync(admin, mine.Id, default);
        Assert.Equal(RecipeVisibility.Rejected, rejected.Visibility);

        // A rejected recipe may be revised and resubmitted.
        var resubmitted = await recipes.SubmitForReviewAsync(user, mine.Id, default);
        Assert.Equal(RecipeVisibility.PendingReview, resubmitted.Visibility);
    }

    [Fact]
    public async Task Non_admin_cannot_access_review_queue_or_approve()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Candidate", 1, false), default);
        await recipes.SubmitForReviewAsync(user, mine.Id, default);

        await Assert.ThrowsAsync<ForbiddenException>(() => recipes.ListReviewQueueAsync(user, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => recipes.ApproveAsync(user, mine.Id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => recipes.RejectAsync(user, mine.Id, default));
    }

    [Fact]
    public async Task Approve_only_valid_from_pending_state()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var mine = await recipes.CreateAsync(user, Recipe("Still Private", 1, false), default);

        // Not yet submitted → cannot approve.
        await Assert.ThrowsAsync<ValidationException>(() => recipes.ApproveAsync(admin, mine.Id, default));
    }

    [Fact]
    public async Task Approved_public_recipe_visible_to_other_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, recipes) = await SetupAsync(db);
        var other = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "other", "other@dev.local", "Other"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        var mine = await recipes.CreateAsync(user, Recipe("Shared", 1, false), default);
        await recipes.SubmitForReviewAsync(user, mine.Id, default);
        await recipes.ApproveAsync(admin, mine.Id, default);

        var otherList = await recipes.ListAsync(other!, null, default);
        Assert.Contains(otherList, r => r.Id == mine.Id);
        // But the other user cannot edit it.
        var seen = await recipes.GetAsync(other!, mine.Id, default);
        Assert.False(seen.CanEdit);
    }
}
