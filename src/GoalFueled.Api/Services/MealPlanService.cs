using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for meal plans: visibility, validation, and the daily/weekly nutrition summary
/// that drives the app's goal-tracking view. Meal plans are personal — there is no app-level sharing —
/// so a plan is visible to, and editable by, its owner only. All authorization is enforced here so the
/// Function endpoints stay thin.
///
/// Nutrition is rolled up from two sources: scheduled meals (each meal's total nutrition via
/// <see cref="MealNutritionCalculator"/>) and untimed items (a meal's total, or a standalone
/// ingredient's per-serving nutrition scaled by quantity, like the recipe roll-up). For a
/// <see cref="MealPlanCadence.Daily"/> plan everything counts toward one all-day summary; for a
/// <see cref="MealPlanCadence.Weekly"/> plan, scheduled meals pinned to a day count toward that day,
/// while day-less scheduled meals and untimed items count toward every day of the week.
/// </summary>
public sealed class MealPlanService
{
    private readonly IMealPlanRepository _repo;
    private readonly IMealRepository _meals;
    private readonly IIngredientRepository _ingredients;
    private readonly MealNutritionCalculator _mealNutrition;

    private static readonly DayOfWeek[] WeekDays = Enum.GetValues<DayOfWeek>();

    public MealPlanService(
        IMealPlanRepository repo, IMealRepository meals,
        IRecipeRepository recipes, IIngredientRepository ingredients)
    {
        _repo = repo;
        _meals = meals;
        _ingredients = ingredients;
        _mealNutrition = new MealNutritionCalculator(recipes, ingredients);
    }

    /// <summary>
    /// Lists meal plans visible to the user. When <paramref name="all"/> is set AND the caller is
    /// acting as an admin, returns every user's plans; otherwise the flag is ignored and the result
    /// stays scoped to the caller's own plans.
    /// </summary>
    public async Task<IReadOnlyList<MealPlanDto>> ListAsync(
        AppUser user, string? search, bool all = false, CancellationToken ct = default)
    {
        var plans = all && user.IsActingAsAdmin
            ? await _repo.ListAllAsync(search, ct)
            : await _repo.ListVisibleAsync(user.Id, search, ct);
        return await ToDtosAsync(plans, user, ct);
    }

    public async Task<MealPlanDto> GetAsync(AppUser user, string id, CancellationToken ct)
    {
        var plan = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal plan not found.");
        EnsureCanView(plan, user);
        return await ToDtoAsync(plan, user, ct);
    }

    public async Task<MealPlanDto> CreateAsync(AppUser user, MealPlanInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);

        var plan = new MealPlan
        {
            Name = input.Name.Trim(),
            Cadence = input.Cadence,
            OwnerUserId = user.Id,
            ScheduledMeals = NormalizeScheduledMeals(input.ScheduledMeals, input.Cadence),
            UntimedItems = NormalizeUntimedItems(input.UntimedItems),
        };

        await _repo.AddAsync(plan, ct);
        return await ToDtoAsync(plan, user, ct);
    }

    public async Task<MealPlanDto> UpdateAsync(AppUser user, string id, MealPlanInput input, CancellationToken ct)
    {
        await ValidateAsync(input, user, ct);
        var plan = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal plan not found.");
        EnsureCanEdit(plan, user);

        plan.Name = input.Name.Trim();
        plan.Cadence = input.Cadence;
        plan.ScheduledMeals = NormalizeScheduledMeals(input.ScheduledMeals, input.Cadence);
        plan.UntimedItems = NormalizeUntimedItems(input.UntimedItems);
        plan.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.UpdateAsync(plan, ct);
        return await ToDtoAsync(plan, user, ct);
    }

    public async Task DeleteAsync(AppUser user, string id, CancellationToken ct)
    {
        var plan = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Meal plan not found.");
        EnsureCanEdit(plan, user);
        await _repo.DeleteAsync(plan, ct);
    }

    // ---- authorization ----
    // Plans are personal: only the owner (or an admin) may view or modify them. There is no app scope.

    private static void EnsureCanView(MealPlan p, AppUser user)
    {
        if (p.OwnerUserId == user.Id) return;
        if (user.IsActingAsAdmin) return; // admins can view all data
        throw new NotFoundException("Meal plan not found.");
    }

    private static void EnsureCanEdit(MealPlan p, AppUser user)
    {
        if (user.IsActingAsAdmin) return; // admins can modify all data
        if (p.OwnerUserId == user.Id) return;
        throw new ForbiddenException("You can only modify your own meal plans.");
    }

    private static bool CanEdit(MealPlan p, AppUser user) =>
        user.IsActingAsAdmin || p.OwnerUserId == user.Id;

    // ---- validation ----

    private async Task ValidateAsync(MealPlanInput input, AppUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ValidationException("Name is required.");
        if (!Enum.IsDefined(typeof(MealPlanCadence), input.Cadence))
            throw new ValidationException("Unknown meal plan cadence.");

        foreach (var scheduled in input.ScheduledMeals ?? Array.Empty<ScheduledMealInput>())
        {
            if (string.IsNullOrWhiteSpace(scheduled.MealId))
                throw new ValidationException("Each scheduled meal must reference a meal.");
            if (scheduled.DayOfWeek is { } dow && !Enum.IsDefined(typeof(DayOfWeek), dow))
                throw new ValidationException("Unknown day of week on a scheduled meal.");

            var meal = await _meals.GetByIdAsync(scheduled.MealId, ct)
                ?? throw new ValidationException($"Meal '{scheduled.MealId}' was not found.");
            if (!CanUseMeal(meal, user))
                throw new ValidationException($"Meal '{meal.Name}' is not available to you.");
        }

        foreach (var item in input.UntimedItems ?? Array.Empty<UntimedItemInput>())
        {
            var hasMeal = !string.IsNullOrWhiteSpace(item.MealId);
            var hasIngredient = !string.IsNullOrWhiteSpace(item.IngredientId);
            if (hasMeal == hasIngredient)
                throw new ValidationException("Each untimed item must reference exactly one of a meal or an ingredient.");

            if (hasMeal)
            {
                var meal = await _meals.GetByIdAsync(item.MealId!, ct)
                    ?? throw new ValidationException($"Meal '{item.MealId}' was not found.");
                if (!CanUseMeal(meal, user))
                    throw new ValidationException($"Meal '{meal.Name}' is not available to you.");
            }
            else
            {
                if (item.Quantity is <= 0)
                    throw new ValidationException("An untimed ingredient quantity must be greater than zero.");
                var ingredient = await _ingredients.GetByIdAsync(item.IngredientId!, ct)
                    ?? throw new ValidationException($"Ingredient '{item.IngredientId}' was not found.");
                if (!CanUseIngredient(ingredient, user))
                    throw new ValidationException($"Ingredient '{ingredient.Name}' is not available to you.");
            }
        }
    }

    private static bool CanUseMeal(Meal m, AppUser user) =>
        m.Scope == OwnershipScope.App || m.OwnerUserId == user.Id || user.IsActingAsAdmin;

    private static bool CanUseIngredient(Ingredient i, AppUser user) =>
        i.Scope == OwnershipScope.App || i.OwnerUserId == user.Id || user.IsActingAsAdmin;

    /// <summary>Drops empty references; clears the day on daily plans (a daily plan applies every day).</summary>
    private static List<ScheduledMeal> NormalizeScheduledMeals(
        IReadOnlyList<ScheduledMealInput>? scheduled, MealPlanCadence cadence)
    {
        if (scheduled is null || scheduled.Count == 0) return new();
        return scheduled
            .Where(s => !string.IsNullOrWhiteSpace(s.MealId))
            .Select(s => new ScheduledMeal
            {
                MealId = s.MealId,
                DayOfWeek = cadence == MealPlanCadence.Daily ? null : s.DayOfWeek,
                Time = s.Time,
            })
            .ToList();
    }

    /// <summary>Drops items that reference neither a meal nor an ingredient.</summary>
    private static List<UntimedItem> NormalizeUntimedItems(IReadOnlyList<UntimedItemInput>? items)
    {
        if (items is null || items.Count == 0) return new();
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.MealId) || !string.IsNullOrWhiteSpace(i.IngredientId))
            .Select(i => new UntimedItem
            {
                MealId = string.IsNullOrWhiteSpace(i.MealId) ? null : i.MealId,
                IngredientId = string.IsNullOrWhiteSpace(i.IngredientId) ? null : i.IngredientId,
                Quantity = i.Quantity,
                Note = i.Note,
            })
            .ToList();
    }

    // ---- nutrition roll-up & summary ----

    /// <summary>
    /// An untimed ingredient item's nutrition: scale the ingredient's per-serving nutrition by
    /// (quantity in base units / serving size in base units), mirroring the recipe item roll-up.
    /// Items in an incompatible unit are treated as the ingredient's own unit (the quantity is taken
    /// directly as ingredient units). Defaults a missing quantity to one serving.
    /// </summary>
    private static IReadOnlyList<NutrientAmount> IngredientItemNutrition(UntimedItem item, Ingredient ingredient)
    {
        if (ingredient.ServingSize <= 0) return Array.Empty<NutrientAmount>();
        // Quantity is expressed in the ingredient's own unit; null means "one serving".
        var quantity = item.Quantity ?? ingredient.ServingSize;
        var servings = quantity / ingredient.ServingSize;
        return NutritionMath.Scale(ingredient.NutritionPerServing, servings);
    }

    private async Task<IReadOnlyList<MealPlanDto>> ToDtosAsync(
        IReadOnlyList<MealPlan> plans, AppUser user, CancellationToken ct)
    {
        var ctx = await ResolveAsync(plans, ct);
        return plans.Select(p => ToDto(p, user, ctx)).ToList();
    }

    private async Task<MealPlanDto> ToDtoAsync(MealPlan plan, AppUser user, CancellationToken ct)
    {
        var ctx = await ResolveAsync(new[] { plan }, ct);
        return ToDto(plan, user, ctx);
    }

    /// <summary>
    /// Loads everything the given plans reference — meals (and their recipes/ingredients) and the
    /// standalone untimed ingredients — into a single lookup context for synchronous mapping.
    /// </summary>
    private async Task<ResolveContext> ResolveAsync(IReadOnlyCollection<MealPlan> plans, CancellationToken ct)
    {
        // Every meal referenced by a scheduled meal or an untimed meal item.
        var mealIds = plans
            .SelectMany(p => p.ScheduledMeals.Select(s => s.MealId)
                .Concat(p.UntimedItems.Where(i => i.MealId is not null).Select(i => i.MealId!)))
            .Distinct();

        var mealsById = new Dictionary<string, Meal>();
        foreach (var id in mealIds)
        {
            var meal = await _meals.GetByIdAsync(id, ct);
            if (meal is not null) mealsById[id] = meal;
        }

        // Recipes + ingredients needed to compute those meals' nutrition.
        var recipesById = await _mealNutrition.ResolveRecipesAsync(mealsById.Values.ToList(), ct);
        var ingredientsById = await _mealNutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);

        // Standalone ingredients referenced by untimed items (may not be covered by any recipe).
        var ingredientIds = plans
            .SelectMany(p => p.UntimedItems)
            .Where(i => i.IngredientId is not null)
            .Select(i => i.IngredientId!)
            .Distinct();
        foreach (var id in ingredientIds)
        {
            if (ingredientsById.ContainsKey(id)) continue;
            var ingredient = await _ingredients.GetByIdAsync(id, ct);
            if (ingredient is not null) ingredientsById[id] = ingredient;
        }

        return new ResolveContext(mealsById, recipesById, ingredientsById);
    }

    private static MealPlanDto ToDto(MealPlan p, AppUser user, ResolveContext ctx)
    {
        var scheduled = p.ScheduledMeals
            .Select(s => new ScheduledMealDto(
                s.MealId, s.DayOfWeek, s.Time,
                ctx.MealsById.TryGetValue(s.MealId, out var m) ? m.Name : null))
            .ToList();

        var untimed = p.UntimedItems
            .Select(i => new UntimedItemDto(
                i.MealId, i.IngredientId, i.Quantity, i.Note, ResolveItemName(i, ctx)))
            .ToList();

        var summary = BuildSummary(p, ctx);

        return new MealPlanDto(
            p.Id, p.Name, p.Cadence, p.OwnerUserId, scheduled, untimed, summary,
            p.CreatedAt, p.UpdatedAt, CanEdit(p, user));
    }

    private static string? ResolveItemName(UntimedItem item, ResolveContext ctx)
    {
        if (item.MealId is not null)
            return ctx.MealsById.TryGetValue(item.MealId, out var m) ? m.Name : null;
        if (item.IngredientId is not null)
            return ctx.IngredientsById.TryGetValue(item.IngredientId, out var i) ? i.Name : null;
        return null;
    }

    /// <summary>Total nutrition contributed by a single scheduled meal or untimed item.</summary>
    private static IReadOnlyList<NutrientAmount> ItemNutrition(string? mealId, UntimedItem? untimed, ResolveContext ctx)
    {
        if (mealId is not null)
        {
            return ctx.MealsById.TryGetValue(mealId, out var meal)
                ? MealNutritionCalculator.ComputeTotal(meal, ctx.RecipesById, ctx.IngredientsById)
                : Array.Empty<NutrientAmount>();
        }
        if (untimed?.IngredientId is { } ingredientId)
        {
            return ctx.IngredientsById.TryGetValue(ingredientId, out var ingredient)
                ? IngredientItemNutrition(untimed, ingredient)
                : Array.Empty<NutrientAmount>();
        }
        return Array.Empty<NutrientAmount>();
    }

    /// <summary>
    /// Builds the daily/weekly nutrition summary. Untimed items and day-less scheduled meals are
    /// consumed every day; day-pinned scheduled meals count toward their day only.
    /// <list type="bullet">
    /// <item>Daily: one summary (DayOfWeek null) = scheduled meals + untimed items.</item>
    /// <item>Weekly: one summary per day of week that has any consumption, plus the weekly total and
    /// the daily average (weekly total ÷ 7).</item>
    /// </list>
    /// </summary>
    private static NutritionSummaryDto BuildSummary(MealPlan p, ResolveContext ctx)
    {
        // Nutrition consumed every day regardless of cadence: untimed items + day-less scheduled meals.
        var everyDay = new List<IEnumerable<NutrientAmount>>();
        foreach (var item in p.UntimedItems)
            everyDay.Add(ItemNutrition(item.MealId, item, ctx));
        foreach (var s in p.ScheduledMeals.Where(s => s.DayOfWeek is null))
            everyDay.Add(ItemNutrition(s.MealId, null, ctx));

        if (p.Cadence == MealPlanCadence.Daily)
        {
            // A daily plan applies every day, so everything (day pins are normalized away) is one summary.
            var dayPinned = p.ScheduledMeals
                .Where(s => s.DayOfWeek is not null)
                .Select(s => ItemNutrition(s.MealId, null, ctx));
            var total = NutritionMath.Sum(everyDay.Concat(dayPinned));
            var days = new List<DailyNutritionDto> { new(null, total) };
            return new NutritionSummaryDto(days, total, total);
        }

        // Weekly: bucket day-pinned meals by day, add the every-day contributions to each bucket.
        var everyDaySum = NutritionMath.Sum(everyDay);
        var byDay = p.ScheduledMeals
            .Where(s => s.DayOfWeek is not null)
            .GroupBy(s => s.DayOfWeek!.Value)
            .ToDictionary(g => g.Key, g => g.Select(s => ItemNutrition(s.MealId, null, ctx)).ToList());

        var dayDtos = new List<DailyNutritionDto>();
        var weekContributions = new List<IEnumerable<NutrientAmount>>();
        foreach (var day in WeekDays)
        {
            var contributions = new List<IEnumerable<NutrientAmount>> { everyDaySum };
            if (byDay.TryGetValue(day, out var pinned)) contributions.AddRange(pinned);
            var dayTotal = NutritionMath.Sum(contributions);
            // Only surface days that actually have consumption.
            if (dayTotal.Count == 0) continue;
            dayDtos.Add(new DailyNutritionDto(day, dayTotal));
            weekContributions.Add(dayTotal);
        }

        var weeklyTotal = NutritionMath.Sum(weekContributions);
        var dailyAverage = NutritionMath.Scale(weeklyTotal, 1.0 / WeekDays.Length);
        return new NutritionSummaryDto(dayDtos, weeklyTotal, dailyAverage);
    }

    /// <summary>Pre-resolved lookups shared while mapping a batch of plans to DTOs.</summary>
    private sealed record ResolveContext(
        IReadOnlyDictionary<string, Meal> MealsById,
        IReadOnlyDictionary<string, Recipe> RecipesById,
        IReadOnlyDictionary<string, Ingredient> IngredientsById);
}
