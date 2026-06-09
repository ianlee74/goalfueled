using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for goal definition and progress tracking. Goals are strictly per-user — each is
/// owned by, and only visible to, its creator (there is no app-level or cross-user view, like
/// <see cref="WeightService"/>).
///
/// <para>Progress is computed live from the user's existing data each time a goal is read, so there is
/// no separate "progress" store to keep in sync:</para>
/// <list type="bullet">
/// <item><b>Weight goals</b> read <see cref="WeightEntry"/> history: the baseline is the earliest weight
/// recorded on/after the goal's start date (falling back to the earliest overall), and the current value
/// is the most recent weight. Progress runs baseline → target.</item>
/// <item><b>Nutrient goals</b> read the user's meal plans, which are the app's model of planned intake.
/// We take the per-day average of the target nutrient across all the user's plans and multiply by the
/// length of the active period (the recurrence window, or start→target span for one-time goals) to get
/// the period's projected intake, which is compared against the target.</item>
/// </list>
///
/// <para><see cref="Goal.ExceedingIsPositive"/> determines whether passing the target is celebrated or
/// flagged, which the computed <see cref="GoalProgressDto.OutcomePositive"/> flag surfaces to the visual.</para>
/// </summary>
public sealed class GoalService
{
    private readonly IGoalRepository _repo;
    private readonly IWeightEntryRepository _weights;
    private readonly IMealPlanRepository _plans;
    private readonly IMealRepository _meals;
    private readonly IIngredientRepository _ingredients;
    private readonly MealNutritionCalculator _mealNutrition;

    public GoalService(
        IGoalRepository repo,
        IWeightEntryRepository weights,
        IMealPlanRepository plans,
        IMealRepository meals,
        IRecipeRepository recipes,
        IIngredientRepository ingredients)
    {
        _repo = repo;
        _weights = weights;
        _plans = plans;
        _meals = meals;
        _ingredients = ingredients;
        _mealNutrition = new MealNutritionCalculator(recipes, ingredients);
    }

    /// <summary>The caller's own goals, most recently created first, each with a fresh progress snapshot.</summary>
    public async Task<IReadOnlyList<GoalDto>> ListAsync(AppUser user, CancellationToken ct)
    {
        var goals = await _repo.ListForUserAsync(user.Id, ct);
        var result = new List<GoalDto>(goals.Count);
        foreach (var goal in goals)
            result.Add(await ToDtoAsync(goal, user, ct));
        return result;
    }

    public async Task<GoalDto> GetAsync(AppUser user, string id, CancellationToken ct)
    {
        var goal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Goal not found.");
        EnsureOwner(goal, user);
        return await ToDtoAsync(goal, user, ct);
    }

    public async Task<GoalDto> CreateAsync(AppUser user, GoalInput input, CancellationToken ct)
    {
        Validate(input);
        var goal = new Goal
        {
            OwnerUserId = user.Id,
            Name = input.Name.Trim(),
            MetricType = input.MetricType,
            NutrientId = input.MetricType == GoalMetricType.Nutrient ? input.NutrientId : null,
            TargetValue = input.TargetValue,
            Cadence = input.Cadence,
            StartDate = input.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            TargetDate = input.Cadence == GoalCadence.OneTime ? input.TargetDate : null,
            RecurrenceDays = input.Cadence == GoalCadence.Recurring ? input.RecurrenceDays : null,
            ExceedingIsPositive = input.ExceedingIsPositive,
        };
        await _repo.AddAsync(goal, ct);
        return await ToDtoAsync(goal, user, ct);
    }

    public async Task<GoalDto> UpdateAsync(AppUser user, string id, GoalInput input, CancellationToken ct)
    {
        Validate(input);
        var goal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Goal not found.");
        EnsureOwner(goal, user);

        goal.Name = input.Name.Trim();
        goal.MetricType = input.MetricType;
        goal.NutrientId = input.MetricType == GoalMetricType.Nutrient ? input.NutrientId : null;
        goal.TargetValue = input.TargetValue;
        goal.Cadence = input.Cadence;
        goal.StartDate = input.StartDate ?? goal.StartDate;
        goal.TargetDate = input.Cadence == GoalCadence.OneTime ? input.TargetDate : null;
        goal.RecurrenceDays = input.Cadence == GoalCadence.Recurring ? input.RecurrenceDays : null;
        goal.ExceedingIsPositive = input.ExceedingIsPositive;
        goal.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.UpdateAsync(goal, ct);
        return await ToDtoAsync(goal, user, ct);
    }

    public async Task DeleteAsync(AppUser user, string id, CancellationToken ct)
    {
        var goal = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Goal not found.");
        EnsureOwner(goal, user);
        await _repo.DeleteAsync(goal, ct);
    }

    // ---- authorization ----

    private static void EnsureOwner(Goal goal, AppUser user)
    {
        // Goals are entirely private; not even admins get a cross-user view.
        if (goal.OwnerUserId != user.Id) throw new NotFoundException("Goal not found.");
    }

    // ---- validation ----

    private static void Validate(GoalInput input)
    {
        InputLimits.RequireText(input.Name, InputLimits.NameMaxLength, "Name");

        if (!Enum.IsDefined(typeof(GoalMetricType), input.MetricType))
            throw new ValidationException("Unknown goal metric type.");
        if (!Enum.IsDefined(typeof(GoalCadence), input.Cadence))
            throw new ValidationException("Unknown goal cadence.");

        if (!(input.TargetValue > 0))
            throw new ValidationException("Target value must be greater than zero.");
        if (input.TargetValue > InputLimits.GoalTargetValueMax)
            throw new ValidationException($"Target value must be {InputLimits.GoalTargetValueMax} or less.");

        if (input.MetricType == GoalMetricType.Nutrient)
        {
            if (string.IsNullOrWhiteSpace(input.NutrientId) || !NutrientCatalog.IsKnown(input.NutrientId))
                throw new ValidationException("A nutrient goal must reference a known nutrient.");
        }

        var start = input.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (input.Cadence == GoalCadence.OneTime)
        {
            if (input.TargetDate is not { } target)
                throw new ValidationException("A one-time goal requires a target date.");
            if (target < start)
                throw new ValidationException("Target date cannot be before the start date.");
        }
        else
        {
            if (input.RecurrenceDays is not { } days)
                throw new ValidationException("A recurring goal requires a recurrence period in days.");
            if (days < 1)
                throw new ValidationException("Recurrence period must be at least one day.");
            if (days > InputLimits.GoalRecurrenceDaysMax)
                throw new ValidationException($"Recurrence period must be {InputLimits.GoalRecurrenceDaysMax} days or less.");
        }
    }

    // ---- DTO mapping & progress ----

    private async Task<GoalDto> ToDtoAsync(Goal goal, AppUser user, CancellationToken ct)
    {
        var nutrient = goal.MetricType == GoalMetricType.Nutrient && goal.NutrientId is not null
            ? NutrientCatalog.Find(goal.NutrientId)
            : null;
        var unit = goal.MetricType == GoalMetricType.Weight ? "lb/kg" : NutrientUnitLabel(nutrient);

        var progress = goal.MetricType == GoalMetricType.Weight
            ? await ComputeWeightProgressAsync(goal, user, ct)
            : await ComputeNutrientProgressAsync(goal, user, ct);

        return new GoalDto(
            goal.Id, goal.Name, goal.MetricType, goal.NutrientId, nutrient?.Name, unit,
            goal.TargetValue, goal.Cadence, goal.StartDate, goal.TargetDate, goal.RecurrenceDays,
            goal.ExceedingIsPositive, progress, goal.CreatedAt, goal.UpdatedAt);
    }

    private static string NutrientUnitLabel(NutrientDefinition? n) => n?.Unit switch
    {
        NutrientUnit.Kcal => "kcal",
        NutrientUnit.Gram => "g",
        NutrientUnit.Milligram => "mg",
        NutrientUnit.Microgram => "mcg",
        NutrientUnit.IU => "IU",
        _ => string.Empty,
    };

    /// <summary>The active period's end deadline plus a human label for the visual.</summary>
    private static (DateOnly? deadline, string label) PeriodFor(Goal goal)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (goal.Cadence == GoalCadence.OneTime)
            return (goal.TargetDate, goal.TargetDate is { } d ? $"by {d:MMM d, yyyy}" : "no deadline");

        // Recurring: find the period that contains today, anchored at the start date.
        var days = goal.RecurrenceDays is > 0 ? goal.RecurrenceDays!.Value : 1;
        var elapsed = today.DayNumber - goal.StartDate.DayNumber;
        var periodIndex = elapsed < 0 ? 0 : elapsed / days;
        var periodEnd = goal.StartDate.AddDays((periodIndex + 1) * days - 1);
        return (periodEnd, $"every {days} day{(days == 1 ? "" : "s")} (this period ends {periodEnd:MMM d})");
    }

    /// <summary>Number of days in the active period, used to scale projected nutrient intake.</summary>
    private static int PeriodDays(Goal goal)
    {
        if (goal.Cadence == GoalCadence.Recurring)
            return goal.RecurrenceDays is > 0 ? goal.RecurrenceDays!.Value : 1;
        // One-time: the whole span from start to target (inclusive), at least one day.
        if (goal.TargetDate is { } target)
            return Math.Max(1, target.DayNumber - goal.StartDate.DayNumber + 1);
        return 1;
    }

    private async Task<GoalProgressDto> ComputeWeightProgressAsync(Goal goal, AppUser user, CancellationToken ct)
    {
        var (deadline, label) = PeriodFor(goal);
        var entries = await _weights.ListForUserAsync(user.Id, ct);

        if (entries.Count == 0)
            return NoData(goal, label, "Record a weight to start tracking this goal.");

        // Baseline: earliest entry on/after the start date, else the earliest overall.
        var ordered = entries.OrderBy(e => e.Date).ToList();
        var baselineEntry = ordered.FirstOrDefault(e => e.Date >= goal.StartDate) ?? ordered[0];
        var baseline = baselineEntry.Value;
        var current = ordered[^1].Value; // latest by date

        return BuildProgress(goal, baseline, current, deadline, label);
    }

    private async Task<GoalProgressDto> ComputeNutrientProgressAsync(Goal goal, AppUser user, CancellationToken ct)
    {
        var (deadline, label) = PeriodFor(goal);
        var nutrientId = goal.NutrientId!;
        var plans = await _plans.ListVisibleAsync(user.Id, search: null, ct);

        if (plans.Count == 0)
            return NoData(goal, label, "Create a meal plan to track this nutrient goal.");

        // Sum the per-day average of this nutrient across all of the user's meal plans (planned intake).
        var dailyIntake = 0.0;
        foreach (var plan in plans)
            dailyIntake += await PlanDailyNutrientAsync(plan, nutrientId, ct);

        if (dailyIntake <= 0)
            return NoData(goal, label,
                "Your meal plans don't supply this nutrient yet — add foods that contain it.");

        // Project intake over the active period; nutrient goals accumulate from zero.
        var periodIntake = dailyIntake * PeriodDays(goal);
        return BuildProgress(goal, baseline: 0, current: periodIntake, deadline, label);
    }

    /// <summary>The per-day amount of one nutrient a plan supplies (its daily-average roll-up value).</summary>
    private async Task<double> PlanDailyNutrientAsync(MealPlan plan, string nutrientId, CancellationToken ct)
    {
        var mealIds = plan.ScheduledMeals.Select(s => s.MealId)
            .Concat(plan.UntimedItems.Where(i => i.MealId is not null).Select(i => i.MealId!))
            .Distinct();

        var mealsById = new Dictionary<string, Meal>();
        foreach (var id in mealIds)
        {
            var meal = await _meals.GetByIdAsync(id, ct);
            if (meal is not null) mealsById[id] = meal;
        }

        var recipesById = await _mealNutrition.ResolveRecipesAsync(mealsById.Values.ToList(), ct);
        var ingredientsById = await _mealNutrition.ResolveIngredientsAsync(recipesById.Values.ToList(), ct);

        foreach (var item in plan.UntimedItems.Where(i => i.IngredientId is not null))
        {
            var id = item.IngredientId!;
            if (ingredientsById.ContainsKey(id)) continue;
            var ingredient = await _ingredients.GetByIdAsync(id, ct);
            if (ingredient is not null) ingredientsById[id] = ingredient;
        }

        var dailyAverage = GoalNutritionRollup.DailyAverage(plan, mealsById, recipesById, ingredientsById);
        return dailyAverage.FirstOrDefault(a => string.Equals(a.NutrientId, nutrientId, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
    }

    private static GoalProgressDto NoData(Goal goal, string label, string message) =>
        new(GoalStatus.NoData, 0, 0, goal.TargetValue, 0, false, goal.ExceedingIsPositive, label, message);

    /// <summary>
    /// Shared progress math: measure travel from <paramref name="baseline"/> toward the target, decide
    /// whether the target was met/exceeded, and translate that into a celebratory or cautionary outcome
    /// using the goal's <see cref="Goal.ExceedingIsPositive"/> flag.
    /// </summary>
    private static GoalProgressDto BuildProgress(
        Goal goal, double baseline, double current, DateOnly? deadline, string label)
    {
        var target = goal.TargetValue;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadlinePassed = deadline is { } d && d < today;

        // Direction of travel: are we increasing or decreasing the metric from the baseline?
        var increasing = target >= baseline;
        var span = Math.Abs(target - baseline);
        var travelled = increasing ? current - baseline : baseline - current;

        var reached = Reached(current, target, increasing);
        var percent = span <= 0
            ? (reached ? 100.0 : 0.0)
            : Math.Clamp(travelled / span * 100.0, 0, 100);
        var exceeded = increasing ? current > target : current < target;

        GoalStatus status;
        if (reached) status = GoalStatus.Achieved;
        else if (deadlinePassed) status = GoalStatus.Missed;
        else status = GoalStatus.InProgress;

        // Outcome polarity: meeting the target is always good. Beyond that, exceeding is good only when
        // ExceedingIsPositive; a missed deadline is a negative outcome.
        bool outcomePositive = status switch
        {
            GoalStatus.Achieved => !exceeded || goal.ExceedingIsPositive,
            GoalStatus.Missed => false,
            _ => true, // in progress, on track by default
        };

        var message = BuildMessage(goal, status, exceeded);

        return new GoalProgressDto(
            status, baseline, current, target,
            Math.Round(percent, 1), exceeded, outcomePositive, label, message);
    }

    private static bool Reached(double current, double target, bool increasing) =>
        increasing ? current >= target : current <= target;

    private static string BuildMessage(Goal goal, GoalStatus status, bool exceeded) => status switch
    {
        GoalStatus.Achieved when exceeded && goal.ExceedingIsPositive =>
            "Smashed it! You blew past your target. 🎉",
        GoalStatus.Achieved when exceeded && !goal.ExceedingIsPositive =>
            "Target met, but you went over — ease off to stay on track. ⚠️",
        GoalStatus.Achieved =>
            "Goal achieved — right on target! 🎉",
        GoalStatus.Missed =>
            "Deadline passed without hitting the target. Keep going! 💪",
        GoalStatus.InProgress =>
            "On your way — keep it up! 🚀",
        _ => "Not enough data yet to show progress.",
    };
}
