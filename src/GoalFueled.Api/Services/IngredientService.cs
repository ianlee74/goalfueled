using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for ingredients: visibility, validation, and the app-vs-user ownership rules.
/// All authorization is enforced here so the Function endpoints stay thin.
/// </summary>
public sealed class IngredientService
{
    private readonly IIngredientRepository _repo;

    public IngredientService(IIngredientRepository repo) => _repo = repo;

    /// <summary>
    /// Lists ingredients visible to the user. When <paramref name="all"/> is set AND the caller is
    /// acting as an admin, returns every user's ingredients; otherwise the flag is ignored and the
    /// result stays scoped to app-level + the caller's own.
    /// </summary>
    public async Task<IReadOnlyList<IngredientDto>> ListAsync(
        AppUser user, string? search, bool all = false, CancellationToken ct = default)
    {
        var items = all && user.IsActingAsAdmin
            ? await _repo.ListAllAsync(search, ct)
            : await _repo.ListVisibleAsync(user.Id, search, ct);
        return items.Select(i => ToDto(i, user)).ToList();
    }

    public async Task<IngredientDto> GetAsync(AppUser user, string id, CancellationToken ct)
    {
        var ingredient = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ingredient not found.");
        EnsureCanView(ingredient, user);
        return ToDto(ingredient, user);
    }

    public async Task<IngredientDto> CreateAsync(AppUser user, IngredientInput input, CancellationToken ct)
    {
        Validate(input);

        var appLevel = input.AppLevel && user.IsActingAsAdmin;
        var ingredient = new Ingredient
        {
            Name = input.Name.Trim(),
            Description = HtmlSanitizer.Sanitize(input.Description),
            ImageUrl = input.ImageUrl,
            Unit = input.Unit,
            ServingSize = input.ServingSize,
            NutritionPerServing = NormalizeNutrition(input.NutritionPerServing),
            Scope = appLevel ? OwnershipScope.App : OwnershipScope.User,
            OwnerUserId = appLevel ? null : user.Id,
        };

        await _repo.AddAsync(ingredient, ct);
        return ToDto(ingredient, user);
    }

    public async Task<IngredientDto> UpdateAsync(AppUser user, string id, IngredientInput input, CancellationToken ct)
    {
        Validate(input);
        var ingredient = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ingredient not found.");
        EnsureCanEdit(ingredient, user);

        ingredient.Name = input.Name.Trim();
        ingredient.Description = HtmlSanitizer.Sanitize(input.Description);
        ingredient.ImageUrl = input.ImageUrl;
        ingredient.Unit = input.Unit;
        ingredient.ServingSize = input.ServingSize;
        ingredient.NutritionPerServing = NormalizeNutrition(input.NutritionPerServing);
        ingredient.UpdatedAt = DateTimeOffset.UtcNow;

        // Admins may move an ingredient to/from app level via the AppLevel flag.
        if (user.IsActingAsAdmin)
        {
            ingredient.Scope = input.AppLevel ? OwnershipScope.App : OwnershipScope.User;
            ingredient.OwnerUserId = input.AppLevel ? null : (ingredient.OwnerUserId ?? user.Id);
        }

        await _repo.UpdateAsync(ingredient, ct);
        return ToDto(ingredient, user);
    }

    public async Task DeleteAsync(AppUser user, string id, CancellationToken ct)
    {
        var ingredient = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ingredient not found.");
        EnsureCanEdit(ingredient, user);
        await _repo.DeleteAsync(ingredient, ct);
    }

    // ---- authorization ----

    private static void EnsureCanView(Ingredient i, AppUser user)
    {
        if (i.Scope == OwnershipScope.App) return;
        if (i.OwnerUserId == user.Id) return;
        if (user.IsActingAsAdmin) return; // admins can view all data
        throw new NotFoundException("Ingredient not found.");
    }

    private static void EnsureCanEdit(Ingredient i, AppUser user)
    {
        if (user.IsActingAsAdmin) return; // admins can modify all data
        if (i.Scope == OwnershipScope.User && i.OwnerUserId == user.Id) return;
        throw new ForbiddenException("You can only modify your own ingredients.");
    }

    private static bool CanEdit(Ingredient i, AppUser user) =>
        user.IsActingAsAdmin || (i.Scope == OwnershipScope.User && i.OwnerUserId == user.Id);

    // ---- validation & mapping ----

    private static void Validate(IngredientInput input)
    {
        InputLimits.RequireText(input.Name, InputLimits.NameMaxLength, "Name");
        InputLimits.LimitText(input.Description, InputLimits.DescriptionMaxLength, "Description");
        InputLimits.LimitText(input.ImageUrl, InputLimits.UrlMaxLength, "Image URL");
        if (input.ServingSize <= 0)
            throw new ValidationException("Serving size must be greater than zero.");
        if (!Enum.IsDefined(typeof(UnitOfMeasure), input.Unit))
            throw new ValidationException("Unknown unit of measure.");
        InputLimits.LimitCount(input.NutritionPerServing?.Count ?? 0, InputLimits.NutritionEntriesMax, "nutrition entries");

        foreach (var amount in input.NutritionPerServing ?? Array.Empty<NutrientAmount>())
        {
            if (!NutrientCatalog.IsKnown(amount.NutrientId))
                throw new ValidationException($"Unknown nutrient '{amount.NutrientId}'.");
            if (amount.Value < 0)
                throw new ValidationException($"Nutrient '{amount.NutrientId}' cannot be negative.");
        }
    }

    /// <summary>Drops unknown nutrients and collapses duplicates, preserving catalog order.</summary>
    private static List<NutrientAmount> NormalizeNutrition(IReadOnlyList<NutrientAmount>? amounts)
    {
        if (amounts is null || amounts.Count == 0) return new();
        var byId = amounts
            .Where(a => NutrientCatalog.IsKnown(a.NutrientId))
            .GroupBy(a => a.NutrientId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        return NutrientCatalog.All
            .Where(def => byId.ContainsKey(def.Id))
            .Select(def => new NutrientAmount(def.Id, byId[def.Id]))
            .ToList();
    }

    private static IngredientDto ToDto(Ingredient i, AppUser user) => new(
        i.Id, i.Name, i.Description, i.ImageUrl, i.Unit, i.ServingSize,
        i.NutritionPerServing, i.Scope, i.OwnerUserId, i.CreatedAt, i.UpdatedAt,
        CanEdit(i, user));
}
