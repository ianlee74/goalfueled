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
        // Favorites first (a stable sort, so the repo's name ordering holds within each group).
        return items
            .OrderByDescending(i => user.IsFavoriteIngredient(i.Id))
            .Select(i => ToDto(i, user))
            .ToList();
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
            Tags = NormalizeTerms(input.Tags),
            Aliases = NormalizeTerms(input.Aliases),
            LocalizedNames = NormalizeLocalizedNames(input.LocalizedNames),
            Scope = appLevel ? OwnershipScope.App : OwnershipScope.User,
            OwnerUserId = appLevel ? null : user.Id,
        };

        await _repo.AddAsync(ingredient, ct);
        return ToDto(ingredient, user);
    }

    /// <summary>
    /// Creates a private clone of an existing ingredient owned by the caller, so a user can take an
    /// app-level (read-only) ingredient and tweak it. Deep-copies every field except identity/ownership:
    /// the clone gets a fresh id, user scope, the caller as owner, and fresh timestamps. Favorite state
    /// is per-user (kept on the user document) and so is naturally not copied.
    /// </summary>
    public async Task<IngredientDto> CustomizeAsync(AppUser user, string id, CancellationToken ct)
    {
        var source = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ingredient not found.");
        EnsureCanView(source, user);

        var clone = new Ingredient
        {
            // New Id is assigned by the entity initializer.
            Name = InputLimits.CustomizedName(source.Name),
            Description = source.Description,
            ImageUrl = source.ImageUrl,
            Unit = source.Unit,
            ServingSize = source.ServingSize,
            NutritionPerServing = source.NutritionPerServing
                .Select(n => new NutrientAmount(n.NutrientId, n.Value)).ToList(),
            Tags = source.Tags is null ? null : new List<string>(source.Tags),
            Aliases = source.Aliases is null ? null : new List<string>(source.Aliases),
            LocalizedNames = source.LocalizedNames?
                .Select(l => new LocalizedName(l.Language, l.Name)).ToList(),
            Scope = OwnershipScope.User,
            OwnerUserId = user.Id,
        };

        await _repo.AddAsync(clone, ct);
        return ToDto(clone, user);
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
        ingredient.Tags = NormalizeTerms(input.Tags);
        ingredient.Aliases = NormalizeTerms(input.Aliases);
        ingredient.LocalizedNames = NormalizeLocalizedNames(input.LocalizedNames);
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

        // Search metadata: bounded lists of short text terms.
        InputLimits.LimitCount(input.Tags?.Count ?? 0, InputLimits.IngredientSearchTermsMax, "tags");
        InputLimits.LimitCount(input.Aliases?.Count ?? 0, InputLimits.IngredientSearchTermsMax, "aliases");
        InputLimits.LimitCount(input.LocalizedNames?.Count ?? 0, InputLimits.IngredientSearchTermsMax, "localized names");
        foreach (var tag in input.Tags ?? Array.Empty<string>())
            InputLimits.LimitText(tag, InputLimits.NameMaxLength, "Tag");
        foreach (var alias in input.Aliases ?? Array.Empty<string>())
            InputLimits.LimitText(alias, InputLimits.NameMaxLength, "Alias");
        foreach (var localized in input.LocalizedNames ?? Array.Empty<LocalizedName>())
        {
            InputLimits.LimitText(localized.Language, LanguageCodeMaxLength, "Language code");
            InputLimits.LimitText(localized.Name, InputLimits.NameMaxLength, "Localized name");
        }

        foreach (var amount in input.NutritionPerServing ?? Array.Empty<NutrientAmount>())
        {
            if (!NutrientCatalog.IsKnown(amount.NutrientId))
                throw new ValidationException($"Unknown nutrient '{amount.NutrientId}'.");
            if (amount.Value < 0)
                throw new ValidationException($"Nutrient '{amount.NutrientId}' cannot be negative.");
        }
    }

    /// <summary>Generous ceiling for a localized-name language code ("es", "pt-BR", …).</summary>
    private const int LanguageCodeMaxLength = 20;

    /// <summary>
    /// Trims search terms (tags/aliases), drops blanks and case-insensitive duplicates.
    /// Returns null when nothing remains so empty lists aren't persisted on every document.
    /// </summary>
    private static List<string>? NormalizeTerms(IReadOnlyList<string>? terms)
    {
        if (terms is null || terms.Count == 0) return null;
        var result = terms
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Trims localized names, drops entries missing a language or name, lower-cases the language
    /// code, and collapses duplicate (language, name) pairs. Null when nothing remains.
    /// </summary>
    private static List<LocalizedName>? NormalizeLocalizedNames(IReadOnlyList<LocalizedName>? names)
    {
        if (names is null || names.Count == 0) return null;
        var result = names
            .Select(n => new LocalizedName((n.Language ?? string.Empty).Trim().ToLowerInvariant(), (n.Name ?? string.Empty).Trim()))
            .Where(n => n.Language.Length > 0 && n.Name.Length > 0)
            .GroupBy(n => (n.Language, Name: n.Name.ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
        return result.Count == 0 ? null : result;
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
        i.Id, i.Name, i.Description, i.ImageUrl,
        i.Tags ?? (IReadOnlyList<string>)Array.Empty<string>(),
        i.Aliases ?? (IReadOnlyList<string>)Array.Empty<string>(),
        i.LocalizedNames ?? (IReadOnlyList<LocalizedName>)Array.Empty<LocalizedName>(),
        i.Unit, i.ServingSize,
        i.NutritionPerServing, i.Scope, i.OwnerUserId, i.CreatedAt, i.UpdatedAt,
        CanEdit(i, user), user.IsFavoriteIngredient(i.Id));
}
