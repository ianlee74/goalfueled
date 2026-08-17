using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;

namespace GoalFueled.Api.Services;

/// <summary>
/// Admin-only user management: listing users, promoting/demoting the App Admin role, and administering
/// feature tags (a user's subscription tier plus their individual feature grants/denials). All
/// authorization is enforced here (acting-admin required) so the Function endpoints stay thin.
/// </summary>
public sealed class UserAdminService
{
    private readonly IUserRepository _users;
    private readonly IIngredientRepository _ingredients;
    private readonly IRecipeRepository _recipes;
    private readonly IMealRepository _meals;
    private readonly IMealPlanRepository _mealPlans;
    private readonly IConsumptionEntryRepository _consumption;
    private readonly IBurnActivityRepository _burns;
    private readonly IWeightEntryRepository _weights;
    private readonly IGoalRepository _goals;
    private readonly IUserStatsRepository _stats;
    private readonly IShareInvitationRepository _shares;

    public UserAdminService(
        IUserRepository users,
        IIngredientRepository ingredients,
        IRecipeRepository recipes,
        IMealRepository meals,
        IMealPlanRepository mealPlans,
        IConsumptionEntryRepository consumption,
        IBurnActivityRepository burns,
        IWeightEntryRepository weights,
        IGoalRepository goals,
        IUserStatsRepository stats,
        IShareInvitationRepository shares)
    {
        _users = users;
        _ingredients = ingredients;
        _recipes = recipes;
        _meals = meals;
        _mealPlans = mealPlans;
        _consumption = consumption;
        _burns = burns;
        _weights = weights;
        _goals = goals;
        _stats = stats;
        _shares = shares;
    }

    /// <summary>Lists all users for the admin user-management screen. Acting-admin only.</summary>
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(AppUser actor, CancellationToken ct)
    {
        EnsureActingAdmin(actor);
        var users = await _users.ListAllAsync(ct);
        return users.Select(u => ToDto(u, actor)).ToList();
    }

    /// <summary>
    /// Promotes or demotes a user's App Admin role. Acting-admin only. Guards against locking the app
    /// out of all admins: the last remaining admin cannot be demoted (whether self or another).
    /// </summary>
    public async Task<AdminUserDto> SetAdminRoleAsync(
        AppUser actor, string userId, bool isAdmin, CancellationToken ct)
    {
        EnsureActingAdmin(actor);

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        // No-op requests short-circuit so we don't write or trip the last-admin guard needlessly.
        if (target.IsAdmin == isAdmin) return ToDto(target, actor);

        if (!isAdmin)
        {
            // Demotion: never allow the last remaining admin to lose the role (would lock everyone out).
            var adminCount = await _users.CountAdminsAsync(ct);
            if (adminCount <= 1)
                throw new ValidationException("Cannot demote the last remaining admin.");
            target.Roles.Remove(AppRole.AppAdmin);
        }
        else if (!target.Roles.Contains(AppRole.AppAdmin))
        {
            target.Roles.Add(AppRole.AppAdmin);
        }

        await _users.UpdateAsync(target, ct);
        return ToDto(target, actor);
    }

    /// <summary>
    /// The full feature-tag catalog, for the admin catalog screen. Acting-admin only. The catalog is
    /// static reference data compiled into the build, so no repository is involved — a feature's status or
    /// minimum tier changes by editing <see cref="FeatureCatalog"/> and deploying, never at runtime.
    /// </summary>
    public IReadOnlyList<FeatureDto> ListFeatures(AppUser actor)
    {
        EnsureActingAdmin(actor);
        return FeatureCatalog.All
            .Select(f => new FeatureDto(f.Key, f.Name, f.Description, f.Status,
                f.MinimumSubscriptionLevel, f.IncludedLevels))
            .ToList();
    }

    /// <summary>
    /// Moves a user to a different subscription tier, which changes the baseline set of features they get.
    /// Acting-admin only — a user can never change their own tier, so this can't be self-served into a
    /// paid feature set.
    /// </summary>
    public async Task<AdminUserDto> SetSubscriptionLevelAsync(
        AppUser actor, string userId, SubscriptionLevel level, CancellationToken ct)
    {
        EnsureActingAdmin(actor);
        if (!Enum.IsDefined(typeof(SubscriptionLevel), level))
            throw new ValidationException("Unknown subscription level.");

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        target.SubscriptionLevel = level;
        await _users.UpdateAsync(target, ct);
        return ToDto(target, actor);
    }

    /// <summary>
    /// Sets one user's individual override for one feature: granted (on top of their tier), denied
    /// (overriding their tier), or inherit (clear the override). Acting-admin only.
    /// </summary>
    /// <remarks>
    /// The key must name a feature in the catalog — an unknown key is rejected rather than stored, so the
    /// grant/denial lists can never accumulate junk that silently does nothing.
    /// </remarks>
    public async Task<AdminUserDto> SetFeatureAccessAsync(
        AppUser actor, string userId, SetFeatureAccessInput input, CancellationToken ct)
    {
        EnsureActingAdmin(actor);
        if (!Enum.IsDefined(typeof(FeatureAccessMode), input.Mode))
            throw new ValidationException("Unknown feature access mode.");
        if (!FeatureCatalog.IsKnown(input.Key))
            throw new ValidationException($"Unknown feature '{input.Key}'.");

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        FeatureAccess.SetOverride(target, input.Key, input.Mode);
        await _users.UpdateAsync(target, ct);
        return ToDto(target, actor);
    }

    /// <summary>
    /// Permanently deletes a user and cascade-deletes everything personal/private they own, while
    /// PRESERVING any content the community now relies on as shared/app-owned (re-homing it to the app).
    /// Acting-admin only.
    ///
    /// <para>Per entity:</para>
    /// <list type="bullet">
    /// <item><b>Ingredients/Recipes</b> — public/approved ones (<c>EffectiveVisibility == Public</c> for
    ///   ingredients; <c>Visibility == Public</c> for recipes) are re-homed to the app (Scope = App,
    ///   OwnerUserId = null, OriginalCreatorUserId kept for credit). All other user-owned ones are deleted.</item>
    /// <item><b>Meals</b> — have no public-review workflow, so every user-scoped meal is private and deleted.
    ///   App-scoped meals are not owned by any user and are untouched.</item>
    /// <item><b>MealPlans, ConsumptionEntries, BurnActivities, WeightEntries, Goals, UserStats</b> — strictly
    ///   personal; all of the user's are deleted.</item>
    /// <item><b>AppUser</b> — the document itself is deleted last (removing the user's favorites with it).</item>
    /// </list>
    /// </summary>
    public async Task DeleteAsync(AppUser actor, string userId, CancellationToken ct)
    {
        EnsureActingAdmin(actor);

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        // An admin operating as this account cannot delete themselves out from under the session.
        if (target.Id == actor.Id)
            throw new ValidationException("You cannot delete your own account.");

        // Never delete the last remaining admin — that would lock the app out of all admin access.
        if (target.IsAdmin && await _users.CountAdminsAsync(ct) <= 1)
            throw new ValidationException("Cannot delete the last remaining admin.");

        // Ingredients: preserve+re-home the public/approved ones; delete the rest the user owns.
        foreach (var ingredient in await _ingredients.ListByOwnerAsync(target.Id, ct))
        {
            if (ingredient.EffectiveVisibility == IngredientVisibility.Public)
                await ReHomeIngredientAsync(ingredient, ct);
            else
                await _ingredients.DeleteAsync(ingredient, ct);
        }

        // Recipes: preserve+re-home the public ones; delete the rest the user owns.
        foreach (var recipe in await _recipes.ListByOwnerAsync(target.Id, ct))
        {
            if (recipe.Visibility == RecipeVisibility.Public)
                await ReHomeRecipeAsync(recipe, ct);
            else
                await _recipes.DeleteAsync(recipe, ct);
        }

        // Meals: no public workflow — every user-scoped meal is private and goes.
        foreach (var meal in await _meals.ListByOwnerAsync(target.Id, ct))
            await _meals.DeleteAsync(meal, ct);

        // Strictly personal data: delete it all.
        foreach (var plan in await _mealPlans.ListByOwnerAsync(target.Id, ct))
            await _mealPlans.DeleteAsync(plan, ct);

        foreach (var entry in await _consumption.ListForUserAsync(target.Id, ct))
            await _consumption.DeleteAsync(entry, ct);

        foreach (var burn in await _burns.ListForUserAsync(target.Id, ct))
            await _burns.DeleteAsync(burn, ct);

        foreach (var weight in await _weights.ListForUserAsync(target.Id, ct))
            await _weights.DeleteAsync(weight, ct);

        foreach (var goal in await _goals.ListForUserAsync(target.Id, ct))
            await _goals.DeleteAsync(goal, ct);

        var stats = await _stats.GetForUserAsync(target.Id, ct);
        if (stats is not null)
            await _stats.DeleteAsync(stats, ct);

        // Sharing: remove every invitation/grant where the user is the owner OR the accepted guest, so no
        // dangling links (that could be opened) or active grants (that could act as a deleted account) remain.
        foreach (var share in await _shares.ListTouchingUserAsync(target.Id, ct))
            await _shares.DeleteAsync(share, ct);

        // Finally remove the user document itself (which also removes their favorites).
        await _users.DeleteAsync(target, ct);
    }

    /// <summary>Re-homes a preserved public ingredient to the app, keeping original-creator credit.</summary>
    private async Task ReHomeIngredientAsync(Ingredient ingredient, CancellationToken ct)
    {
        // Keep the original creator credit (set it from the departing owner if not already recorded).
        ingredient.OriginalCreatorUserId ??= ingredient.OwnerUserId;
        ingredient.Scope = OwnershipScope.App;
        ingredient.OwnerUserId = null;
        ingredient.Visibility = IngredientVisibility.Public;
        ingredient.UpdatedAt = DateTimeOffset.UtcNow;
        await _ingredients.UpdateAsync(ingredient, ct);
    }

    /// <summary>Re-homes a preserved public recipe to the app, keeping original-creator credit.</summary>
    private async Task ReHomeRecipeAsync(Recipe recipe, CancellationToken ct)
    {
        recipe.OriginalCreatorUserId ??= recipe.OwnerUserId;
        recipe.Scope = OwnershipScope.App;
        recipe.OwnerUserId = null;
        recipe.Visibility = RecipeVisibility.Public;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        await _recipes.UpdateAsync(recipe, ct);
    }

    private static void EnsureActingAdmin(AppUser user)
    {
        if (!user.IsActingAsAdmin)
            throw new ForbiddenException("This action requires admin access.");
    }

    private static AdminUserDto ToDto(AppUser u, AppUser actor) => new(
        u.Id, u.Email, u.DisplayName, u.AvatarUrl, u.Roles, u.IsAdmin, u.Id == actor.Id,
        // Legacy documents have no stored tier/overrides; EffectiveSubscriptionLevel resolves null to Free
        // and the null lists surface as empty so the admin screen never has to special-case them.
        u.EffectiveSubscriptionLevel,
        u.GrantedFeatureKeys ?? new List<string>(),
        u.DeniedFeatureKeys ?? new List<string>(),
        FeatureAccess.ResolveKeys(u));
}
