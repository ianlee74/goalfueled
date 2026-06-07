using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for recipes: visibility, validation, the app-vs-user ownership rules, the
/// nutrition roll-up, and the public-recipe review workflow. All authorization is enforced here so
/// the Function endpoints stay thin. Needs <see cref="IIngredientRepository"/> to resolve referenced
/// ingredients when computing nutrition and item names.
/// </summary>
public sealed class RecipeService
{
    private readonly IRecipeRepository _repo;
    private readonly IIngredientRepository _ingredients;
    private readonly RecipeNutritionCalculator _nutrition;

    public RecipeService(IRecipeRepository repo, IIngredientRepository ingredients)
    {
        _repo = repo;
        _ingredients = ingredients;
        _nutrition = new RecipeNutritionCalculator(ingredients);
    }

    public async Task<IReadOnlyList<RecipeDto>> ListAsync(AppUser user, string? search, CancellationToken ct)
    {
        var recipes = await _repo.ListVisibleAsync(user.Id, search, ct);
        return await ToDtosAsync(recipes, user, ct);
    }

    public async Task<RecipeDto> GetAsync(AppUser user, string id, CancellationToken ct)
    {
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");
        EnsureCanView(recipe, user);
        return await ToDtoAsync(recipe, user, ct);
    }

    /// <summary>Recipes awaiting review. Admin-only (acting as admin).</summary>
    public async Task<IReadOnlyList<RecipeDto>> ListReviewQueueAsync(AppUser user, CancellationToken ct)
    {
        EnsureActingAdmin(user);
        var recipes = await _repo.ListPendingReviewAsync(ct);
        return await ToDtosAsync(recipes, user, ct);
    }

    public async Task<RecipeDto> CreateAsync(AppUser user, RecipeInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);

        var appLevel = input.AppLevel && user.IsActingAsAdmin;
        var recipe = new Recipe
        {
            Name = input.Name.Trim(),
            Description = input.Description,
            ImageUrl = input.ImageUrl,
            Items = NormalizeItems(input.Items),
            Servings = input.Servings,
            Scope = appLevel ? OwnershipScope.App : OwnershipScope.User,
            OwnerUserId = appLevel ? null : user.Id,
            OriginalCreatorUserId = user.Id,
            // App-level recipes are inherently public; user recipes start private.
            Visibility = appLevel ? RecipeVisibility.Public : RecipeVisibility.Private,
        };

        await _repo.AddAsync(recipe, ct);
        return await ToDtoAsync(recipe, user, ct);
    }

    public async Task<RecipeDto> UpdateAsync(AppUser user, string id, RecipeInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");
        EnsureCanEdit(recipe, user);

        recipe.Name = input.Name.Trim();
        recipe.Description = input.Description;
        recipe.ImageUrl = input.ImageUrl;
        recipe.Items = NormalizeItems(input.Items);
        recipe.Servings = input.Servings;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;

        // Admins may move a recipe to/from app level via the AppLevel flag. A recipe that is already
        // public can never be made private again (see review-workflow rules below).
        if (user.IsActingAsAdmin)
        {
            recipe.Scope = input.AppLevel ? OwnershipScope.App : OwnershipScope.User;
            recipe.OwnerUserId = input.AppLevel ? null : (recipe.OwnerUserId ?? recipe.OriginalCreatorUserId);
            if (input.AppLevel) recipe.Visibility = RecipeVisibility.Public;
        }

        await _repo.UpdateAsync(recipe, ct);
        return await ToDtoAsync(recipe, user, ct);
    }

    public async Task DeleteAsync(AppUser user, string id, CancellationToken ct)
    {
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");
        EnsureCanEdit(recipe, user);
        await _repo.DeleteAsync(recipe, ct);
    }

    // ---- review workflow ----

    /// <summary>
    /// User action: submit a private recipe for review so it can become public.
    /// Only the owner of a <see cref="RecipeVisibility.Private"/> (or previously rejected) recipe may
    /// do this; it transitions to <see cref="RecipeVisibility.PendingReview"/>.
    /// </summary>
    public async Task<RecipeDto> SubmitForReviewAsync(AppUser user, string id, CancellationToken ct)
    {
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");

        if (!(recipe.Scope == OwnershipScope.User && recipe.OwnerUserId == user.Id))
            throw new ForbiddenException("You can only submit your own recipes for review.");
        if (recipe.Visibility == RecipeVisibility.Public)
            throw new ValidationException("This recipe is already public.");
        if (recipe.Visibility == RecipeVisibility.PendingReview)
            throw new ValidationException("This recipe is already awaiting review.");

        recipe.Visibility = RecipeVisibility.PendingReview;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(recipe, ct);
        return await ToDtoAsync(recipe, user, ct);
    }

    /// <summary>
    /// Admin action: approve a pending recipe, making it <see cref="RecipeVisibility.Public"/>.
    /// Once public a recipe can never become private again; the original creator stays credited.
    /// </summary>
    public async Task<RecipeDto> ApproveAsync(AppUser user, string id, CancellationToken ct)
    {
        EnsureActingAdmin(user);
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");
        if (recipe.Visibility != RecipeVisibility.PendingReview)
            throw new ValidationException("Only recipes awaiting review can be approved.");

        recipe.Visibility = RecipeVisibility.Public;
        // Preserve the credit for whoever created it; the recipe stays user-scoped/owned.
        recipe.OriginalCreatorUserId ??= recipe.OwnerUserId;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(recipe, ct);
        return await ToDtoAsync(recipe, user, ct);
    }

    /// <summary>
    /// Admin action: reject a pending recipe, returning it to a private-ish
    /// <see cref="RecipeVisibility.Rejected"/> state visible only to its owner.
    /// </summary>
    public async Task<RecipeDto> RejectAsync(AppUser user, string id, CancellationToken ct)
    {
        EnsureActingAdmin(user);
        var recipe = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Recipe not found.");
        if (recipe.Visibility != RecipeVisibility.PendingReview)
            throw new ValidationException("Only recipes awaiting review can be rejected.");

        recipe.Visibility = RecipeVisibility.Rejected;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(recipe, ct);
        return await ToDtoAsync(recipe, user, ct);
    }

    // ---- authorization ----

    private static void EnsureCanView(Recipe r, AppUser user)
    {
        if (r.Scope == OwnershipScope.App) return;
        if (r.Visibility == RecipeVisibility.Public) return;
        if (r.OwnerUserId == user.Id) return;
        if (user.IsActingAsAdmin) return; // admins can view all data
        throw new NotFoundException("Recipe not found.");
    }

    private static void EnsureCanEdit(Recipe r, AppUser user)
    {
        if (user.IsActingAsAdmin) return; // admins can modify all data
        if (r.Scope == OwnershipScope.User && r.OwnerUserId == user.Id) return;
        throw new ForbiddenException("You can only modify your own recipes.");
    }

    private static void EnsureActingAdmin(AppUser user)
    {
        if (!user.IsActingAsAdmin)
            throw new ForbiddenException("This action requires admin access.");
    }

    private static bool CanEdit(Recipe r, AppUser user) =>
        user.IsActingAsAdmin || (r.Scope == OwnershipScope.User && r.OwnerUserId == user.Id);

    /// <summary>The owner of a non-public recipe may submit it for public review.</summary>
    private static bool CanSubmitForReview(Recipe r, AppUser user) =>
        r.Scope == OwnershipScope.User && r.OwnerUserId == user.Id &&
        r.Visibility is RecipeVisibility.Private or RecipeVisibility.Rejected;

    private static bool CanReview(Recipe r, AppUser user) =>
        user.IsActingAsAdmin && r.Visibility == RecipeVisibility.PendingReview;

    // ---- validation ----

    private async Task ValidateAsync(RecipeInput input, AppUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ValidationException("Name is required.");
        if (input.Servings <= 0)
            throw new ValidationException("Servings must be greater than zero.");

        foreach (var item in input.Items ?? Array.Empty<RecipeItemInput>())
        {
            if (string.IsNullOrWhiteSpace(item.IngredientId))
                throw new ValidationException("Each recipe item must reference an ingredient.");
            if (item.Quantity <= 0)
                throw new ValidationException("Each recipe item quantity must be greater than zero.");
            if (!Enum.IsDefined(typeof(UnitOfMeasure), item.Unit))
                throw new ValidationException("Unknown unit of measure on a recipe item.");

            var ingredient = await _ingredients.GetByIdAsync(item.IngredientId, ct)
                ?? throw new ValidationException($"Ingredient '{item.IngredientId}' was not found.");
            // The referenced ingredient must be one the caller can actually see.
            if (!CanUseIngredient(ingredient, user))
                throw new ValidationException($"Ingredient '{ingredient.Name}' is not available to you.");
            if (!Units.AreCompatible(item.Unit, ingredient.Unit))
                throw new ValidationException(
                    $"Unit '{item.Unit}' is not compatible with ingredient '{ingredient.Name}' (measured in {ingredient.Unit}).");
        }
    }

    private static bool CanUseIngredient(Ingredient i, AppUser user) =>
        i.Scope == OwnershipScope.App || i.OwnerUserId == user.Id || user.IsActingAsAdmin;

    /// <summary>Drops empty references and keeps only the persisted item fields.</summary>
    private static List<RecipeItem> NormalizeItems(IReadOnlyList<RecipeItemInput>? items)
    {
        if (items is null || items.Count == 0) return new();
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.IngredientId))
            .Select(i => new RecipeItem
            {
                IngredientId = i.IngredientId,
                Quantity = i.Quantity,
                Unit = i.Unit,
            })
            .ToList();
    }

    // ---- nutrition roll-up & mapping ----
    // The actual ingredient resolution + conversion math lives in RecipeNutritionCalculator so the
    // Meals feature can reuse each recipe's per-serving nutrition without duplicating it.

    private async Task<IReadOnlyList<RecipeDto>> ToDtosAsync(
        IReadOnlyList<Recipe> recipes, AppUser user, CancellationToken ct)
    {
        var byId = await _nutrition.ResolveIngredientsAsync(recipes, ct);
        return recipes.Select(r => ToDto(r, user, byId)).ToList();
    }

    private async Task<RecipeDto> ToDtoAsync(Recipe recipe, AppUser user, CancellationToken ct)
    {
        var byId = await _nutrition.ResolveIngredientsAsync(new[] { recipe }, ct);
        return ToDto(recipe, user, byId);
    }

    private static RecipeDto ToDto(Recipe r, AppUser user, IReadOnlyDictionary<string, Ingredient> byId)
    {
        var total = RecipeNutritionCalculator.ComputeTotal(r, byId);
        var perServing = RecipeNutritionCalculator.ComputePerServing(r, byId);
        var items = r.Items
            .Select(i => new RecipeItemDto(
                i.IngredientId, i.Quantity, i.Unit,
                byId.TryGetValue(i.IngredientId, out var ing) ? ing.Name : null))
            .ToList();

        return new RecipeDto(
            r.Id, r.Name, r.Description, r.ImageUrl, items, r.Servings,
            r.Scope, r.OwnerUserId, r.OriginalCreatorUserId, r.Visibility,
            total, perServing, r.CreatedAt, r.UpdatedAt,
            CanEdit(r, user), CanSubmitForReview(r, user), CanReview(r, user));
    }
}
