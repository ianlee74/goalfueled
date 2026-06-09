using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Computes the per-day average nutrition a meal plan supplies, mirroring the daily-average figure that
/// <see cref="MealPlanService"/> reports for goal tracking. Extracted here so <see cref="GoalService"/>
/// can roll up a user's planned daily intake of a nutrient without duplicating the plan summary logic.
///
/// <para>Daily plans contribute their whole all-day total each day. Weekly plans average their week:
/// untimed items and day-less scheduled meals count every day; day-pinned meals count on their day only,
/// and the result is divided across the seven days of the week (matching the plan summary's DailyAverage).</para>
/// </summary>
internal static class GoalNutritionRollup
{
    private static readonly DayOfWeek[] WeekDays = Enum.GetValues<DayOfWeek>();

    public static IReadOnlyList<NutrientAmount> DailyAverage(
        MealPlan plan,
        IReadOnlyDictionary<string, Meal> mealsById,
        IReadOnlyDictionary<string, Recipe> recipesById,
        IReadOnlyDictionary<string, Ingredient> ingredientsById)
    {
        // Consumed every day regardless of cadence: untimed items + day-less scheduled meals.
        var everyDay = new List<IEnumerable<NutrientAmount>>();
        foreach (var item in plan.UntimedItems)
            everyDay.Add(ItemNutrition(item.MealId, item, mealsById, recipesById, ingredientsById));
        foreach (var s in plan.ScheduledMeals.Where(s => s.DayOfWeek is null))
            everyDay.Add(ItemNutrition(s.MealId, null, mealsById, recipesById, ingredientsById));

        if (plan.Cadence == MealPlanCadence.Daily)
        {
            // A daily plan applies every day; everything is one day's total (= the daily average).
            var dayPinned = plan.ScheduledMeals
                .Where(s => s.DayOfWeek is not null)
                .Select(s => ItemNutrition(s.MealId, null, mealsById, recipesById, ingredientsById));
            return NutritionMath.Sum(everyDay.Concat(dayPinned));
        }

        // Weekly: build each day's total, then average across all seven days of the week.
        var everyDaySum = NutritionMath.Sum(everyDay);
        var byDay = plan.ScheduledMeals
            .Where(s => s.DayOfWeek is not null)
            .GroupBy(s => s.DayOfWeek!.Value)
            .ToDictionary(g => g.Key,
                g => g.Select(s => ItemNutrition(s.MealId, null, mealsById, recipesById, ingredientsById)).ToList());

        var weekContributions = new List<IEnumerable<NutrientAmount>>();
        foreach (var day in WeekDays)
        {
            var contributions = new List<IEnumerable<NutrientAmount>> { everyDaySum };
            if (byDay.TryGetValue(day, out var pinned)) contributions.AddRange(pinned);
            var dayTotal = NutritionMath.Sum(contributions);
            if (dayTotal.Count == 0) continue;
            weekContributions.Add(dayTotal);
        }

        var weeklyTotal = NutritionMath.Sum(weekContributions);
        return NutritionMath.Scale(weeklyTotal, 1.0 / WeekDays.Length);
    }

    private static IReadOnlyList<NutrientAmount> ItemNutrition(
        string? mealId, UntimedItem? untimed,
        IReadOnlyDictionary<string, Meal> mealsById,
        IReadOnlyDictionary<string, Recipe> recipesById,
        IReadOnlyDictionary<string, Ingredient> ingredientsById)
    {
        if (mealId is not null)
        {
            return mealsById.TryGetValue(mealId, out var meal)
                ? MealNutritionCalculator.ComputeTotal(meal, recipesById, ingredientsById)
                : Array.Empty<NutrientAmount>();
        }
        if (untimed?.IngredientId is { } ingredientId)
        {
            return ingredientsById.TryGetValue(ingredientId, out var ingredient)
                ? IngredientItemNutrition(untimed, ingredient)
                : Array.Empty<NutrientAmount>();
        }
        return Array.Empty<NutrientAmount>();
    }

    /// <summary>Scales an ingredient's per-serving nutrition by the untimed item's quantity (in servings).</summary>
    private static IReadOnlyList<NutrientAmount> IngredientItemNutrition(UntimedItem item, Ingredient ingredient)
    {
        if (ingredient.ServingSize <= 0) return Array.Empty<NutrientAmount>();
        var quantity = item.Quantity ?? ingredient.ServingSize;
        var servings = quantity / ingredient.ServingSize;
        return NutritionMath.Scale(ingredient.NutritionPerServing, servings);
    }
}
