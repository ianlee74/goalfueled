using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Aggregates the landing-page dashboard from the existing feature services so the client makes a single
/// call. It owns no roll-up math of its own — it reuses <see cref="MealPlanService"/> (today's/forward
/// planned meals), <see cref="ConsumptionService"/> (planned-day completion + per-day fuel), <see
/// cref="BurnService"/> (per-day burn), <see cref="KindleCostCalculator"/> (passive cost), and
/// <see cref="WeightProgressEstimator"/> (net → estimated weight change). Each piece degrades gracefully
/// so the dashboard renders for a brand-new user with no plan, no profile, and nothing logged.
/// </summary>
public sealed class DashboardService
{
    /// <summary>Days in the progress chart window (today plus the previous six).</summary>
    public const int ProgressWindowDays = 7;

    /// <summary>How many upcoming meals the dashboard surfaces.</summary>
    public const int UpcomingMealCount = 3;

    /// <summary>How many plan days ahead "upcoming" rolls into when today has too few meals left.</summary>
    private const int UpcomingLookaheadDays = 7;

    private const string CaloriesNutrientId = "calories";

    private readonly MealPlanService _plans;
    private readonly ConsumptionService _consumption;
    private readonly BurnService _burn;
    private readonly IWeightEntryRepository _weights;

    public DashboardService(
        MealPlanService plans,
        ConsumptionService consumption,
        BurnService burn,
        IWeightEntryRepository weights)
    {
        _plans = plans;
        _consumption = consumption;
        _burn = burn;
        _weights = weights;
    }

    /// <summary>Builds the dashboard for a day (defaulting to today). <paramref name="now"/> lets tests pin "now".</summary>
    public async Task<DashboardDto> GetAsync(AppUser user, DateOnly? date, DateTimeOffset? now, CancellationToken ct)
    {
        var asOf = now ?? DateTimeOffset.UtcNow;
        var today = date ?? DateOnly.FromDateTime(asOf.UtcDateTime);

        // The planned day reuses The Log's logic: tasks (with completion state) + the weight-due task.
        var plannedToday = await _consumption.GetPlannedDayAsync(user, today, ct);

        var upcoming = await BuildUpcomingAsync(user, plannedToday, today, TimeOnly.FromDateTime(asOf.UtcDateTime), ct);
        var weightDue = BuildWeightDue(plannedToday.WeightTask);
        var progress = await BuildProgressAsync(user, today, ct);

        return new DashboardDto(
            today, plannedToday.PlanId, plannedToday.PlanName, upcoming, weightDue, progress);
    }

    // ---- next-3 upcoming meals ----

    /// <summary>
    /// The next (up to) three not-yet-completed planned meals. Today's incomplete tasks scheduled at or
    /// after <paramref name="nowTime"/> come first (timed before untimed, earliest first); if fewer than
    /// three remain today, we roll forward into subsequent plan days (up to a week) taking that day's meals
    /// in schedule order. Completed tasks are skipped on today (forward days have no completion state yet,
    /// so all of their meals count as upcoming).
    /// </summary>
    private async Task<IReadOnlyList<UpcomingMealDto>> BuildUpcomingAsync(
        AppUser user, PlannedDayDto plannedToday, DateOnly today, TimeOnly nowTime, CancellationToken ct)
    {
        if (plannedToday.PlanId is null) return Array.Empty<UpcomingMealDto>();
        var planId = plannedToday.PlanId;

        var result = new List<UpcomingMealDto>();

        // Today: incomplete tasks at/after now, timed (by time) before untimed ("anytime").
        var todayUpcoming = plannedToday.Tasks
            .Where(t => !t.Done)
            .Where(t => t.Time is null || t.Time >= nowTime)
            .OrderBy(t => t.Time is null) // timed first (false sorts before true)
            .ThenBy(t => t.Time ?? TimeOnly.MaxValue);
        foreach (var t in todayUpcoming)
        {
            result.Add(ToUpcoming(t, today, isLaterDay: false));
            if (result.Count == UpcomingMealCount) return result;
        }

        // Roll forward into later plan days until we have three (or run out of the lookahead window).
        for (var offset = 1; offset <= UpcomingLookaheadDays && result.Count < UpcomingMealCount; offset++)
        {
            var day = today.AddDays(offset);
            var dayTasks = (await _plans.GetPlannedMealsForDateAsync(user, planId, day, ct))
                .OrderBy(t => t.Time is null)
                .ThenBy(t => t.Time ?? TimeOnly.MaxValue);
            foreach (var t in dayTasks)
            {
                result.Add(ToUpcoming(t, day, isLaterDay: true));
                if (result.Count == UpcomingMealCount) return result;
            }
        }

        return result;
    }

    private static UpcomingMealDto ToUpcoming(PlannedMealTaskDto task, DateOnly day, bool isLaterDay) =>
        new(task.Ref, task.MealId, task.MealName, day, task.Time, Calories(task.Nutrition), isLaterDay);

    // ---- weight due today ----

    private static WeightDueDto? BuildWeightDue(WeightTaskDto? weightTask)
    {
        // GetPlannedDayAsync returns a non-null WeightTask only on a due day, so its presence == "due today".
        if (weightTask is null) return null;
        return new WeightDueDto(weightTask.Done, weightTask.RecordedValue, weightTask.RecordedUnit);
    }

    // ---- 7-day fuel/burn/kindle/net series ----

    /// <summary>
    /// Assembles the 7-day series. Fuel comes from logged consumption per day, burn from logged activity
    /// per day, and kindle is the user's daily passive cost (Mifflin–St Jeor BMR × 1.2). Kindle is derived
    /// once from the user's profile + latest recorded weight and applied to every day in the window
    /// (documented simplification: we use the current weight for past days rather than reconstructing each
    /// day's historical weight). When the profile is incomplete the kindle term is 0 and the net is
    /// fuel − burn only, with <see cref="ProgressSeriesDto.KindleAvailable"/> false so the UI nudges the user.
    /// </summary>
    private async Task<ProgressSeriesDto> BuildProgressAsync(AppUser user, DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-(ProgressWindowDays - 1));
        var to = today;

        var fuelByDay = await _consumption.DailyNutritionInRangeAsync(user, from, to, ct);
        var burnByDay = await _burn.DailyBurnInRangeAsync(user, from, to, ct);

        // The user's current weight is the latest recorded entry; it also drives the kindle calculation.
        var history = await _weights.ListForUserAsync(user.Id, ct); // most-recent day first
        (double Value, WeightUnit Unit)? latest = history.Count > 0
            ? (history[0].Value, history[0].Unit)
            : null;
        var weightUnit = latest?.Unit ?? WeightUnit.Pound;

        var kindle = KindleCostCalculator.TryCompute(
            user.SexAtBirth, user.HeightCm, user.DateOfBirth, latest, today);
        var kindleAvailable = kindle is not null;
        var kindlePerDay = kindle ?? 0;

        var days = new List<ProgressDayDto>(ProgressWindowDays);
        var anyData = false;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var fuel = fuelByDay.TryGetValue(day, out var f) ? Calories(f) : 0;
            var burn = burnByDay.TryGetValue(day, out var b) ? b : 0;
            if (fuel > 0 || burn > 0 || kindleAvailable) anyData = true;

            var net = fuel - burn - kindlePerDay;
            days.Add(new ProgressDayDto(day, fuel, burn, kindlePerDay, net));
        }

        var cumulativeNet = WeightProgressEstimator.CumulativeNet(days.Select(d => d.Net));
        var poundsLost = WeightProgressEstimator.PoundsLost(cumulativeNet);
        var weightLost = WeightProgressEstimator.InUnit(poundsLost, weightUnit);

        return new ProgressSeriesDto(
            days, cumulativeNet, weightLost, weightUnit, kindleAvailable, anyData);
    }

    private static double Calories(IReadOnlyList<NutrientAmount> nutrition) =>
        nutrition.FirstOrDefault(a =>
            string.Equals(a.NutrientId, CaloriesNutrientId, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
}
