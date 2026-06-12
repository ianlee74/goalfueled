using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services;

/// <summary>
/// Read/summary side of gamification (DESIGN.md §3.3/§4): the level/FP snapshot, the running
/// week score, and the weekly "your week in fuel" review with its one-time completion bonus.
///
/// <para>The week score reflects <b>engagement</b>, not outcomes: logging consistency (50),
/// budget adherence (30), and weigh-ins on schedule (20). Weeks run Monday–Sunday.</para>
/// </summary>
public sealed class GamificationService
{
    /// <summary>Base FP for completing a weekly review (the score is added on top, max +100).</summary>
    public const int ReviewBasePoints = 150;

    /// <summary>Adherence band (± kcal of net) for users without a calorie cap or direction.</summary>
    private const int MaintainBandKcal = 250;

    private const string CaloriesNutrientId = "calories";
    private const double PoundsPerKilogram = 2.2046226218;

    private readonly IUserStatsRepository _stats;
    private readonly IConsumptionEntryRepository _consumptionEntries;
    private readonly ConsumptionService _consumption;
    private readonly BurnService _burn;
    private readonly IWeightEntryRepository _weights;
    private readonly IGoalRepository _goals;

    public GamificationService(
        IUserStatsRepository stats,
        IConsumptionEntryRepository consumptionEntries,
        ConsumptionService consumption,
        BurnService burn,
        IWeightEntryRepository weights,
        IGoalRepository goals)
    {
        _stats = stats;
        _consumptionEntries = consumptionEntries;
        _consumption = consumption;
        _burn = burn;
        _weights = weights;
        _goals = goals;
    }

    /// <summary>The caller's gamification snapshot. <paramref name="now"/> lets tests pin "today".</summary>
    public async Task<GamificationSummaryDto> GetSummaryAsync(AppUser user, DateTimeOffset? now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime((now ?? DateTimeOffset.UtcNow).UtcDateTime);
        var stats = await _stats.GetForUserAsync(user.Id, ct);
        var points = stats?.FuelPoints ?? 0;
        var (number, rank, floor, next) = FuelLevels.ForPoints(points);

        var currentWeekStart = FuelLevels.WeekStartOf(today);
        var week = await ComputeWeekAsync(user, stats, currentWeekStart, today, ct);

        var lastFinishedWeekStart = currentWeekStart.AddDays(-7);
        var reviews = stats?.WeeklyReviews ?? new List<WeeklyReviewRecord>();
        var pending = reviews.Any(r => r.WeekStart == lastFinishedWeekStart) ? (DateOnly?)null : lastFinishedWeekStart;
        var last = reviews.OrderByDescending(r => r.WeekStart).FirstOrDefault();

        return new GamificationSummaryDto(
            points,
            new LevelDto(number, rank, floor, next),
            currentWeekStart,
            week.Score,
            pending,
            last is null ? null : new CompletedReviewDto(last.WeekStart, last.Score, last.PointsAwarded, last.CompletedAt));
    }

    /// <summary>
    /// The review for the week starting <paramref name="weekStart"/> (any non-Monday is normalized
    /// back to its Monday; null means the most recent finished week).
    /// </summary>
    public async Task<WeeklyReviewDto> GetReviewAsync(AppUser user, DateOnly? weekStart, DateTimeOffset? now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime((now ?? DateTimeOffset.UtcNow).UtcDateTime);
        var start = FuelLevels.WeekStartOf(weekStart ?? FuelLevels.WeekStartOf(today).AddDays(-7));
        if (start > today)
            throw new ValidationException("That week hasn't started yet.");

        var stats = await _stats.GetForUserAsync(user.Id, ct);
        var week = await ComputeWeekAsync(user, stats, start, today, ct);
        return week.Dto;
    }

    /// <summary>
    /// Completes (claims) the review for a finished week, awarding the base bonus + the score once.
    /// Idempotent: re-completing returns the already-recorded review.
    /// </summary>
    public async Task<WeeklyReviewDto> CompleteReviewAsync(AppUser user, CompleteReviewInput input, DateTimeOffset? now, CancellationToken ct)
    {
        var asOf = now ?? DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var start = FuelLevels.WeekStartOf(input.WeekStart);
        if (start.AddDays(7) > today)
            throw new ValidationException("A week can be reviewed only once it has finished.");

        var stats = await _stats.GetForUserAsync(user.Id, ct);
        if (stats?.WeeklyReviews.Any(r => r.WeekStart == start) == true)
            return (await ComputeWeekAsync(user, stats, start, today, ct)).Dto;

        var isNew = stats is null;
        stats ??= new UserStats { OwnerUserId = user.Id };

        var week = await ComputeWeekAsync(user, stats, start, today, ct);
        var bonus = ReviewBasePoints + week.Score;
        stats.FuelPoints += bonus;
        stats.WeeklyReviews.Add(new WeeklyReviewRecord
        {
            WeekStart = start,
            Score = week.Score,
            PointsAwarded = bonus,
            CompletedAt = asOf,
        });
        stats.UpdatedAt = asOf;

        if (isNew) await _stats.AddAsync(stats, ct);
        else await _stats.UpdateAsync(stats, ct);

        return (await ComputeWeekAsync(user, stats, start, today, ct)).Dto;
    }

    // ---- week computation ----

    private sealed record WeekComputation(int Score, WeeklyReviewDto Dto);

    private async Task<WeekComputation> ComputeWeekAsync(
        AppUser user, UserStats? stats, DateOnly weekStart, DateOnly today, CancellationToken ct)
    {
        var weekEnd = weekStart.AddDays(6);
        // For the in-progress week only the elapsed days count (and divide the score).
        var effectiveEnd = weekEnd < today ? weekEnd : today;
        var daysCounted = Math.Max(1, effectiveEnd.DayNumber - weekStart.DayNumber + 1);

        var fuelByDay = await _consumption.DailyNutritionInRangeAsync(user, weekStart, effectiveEnd, ct);
        var burnByDay = await _burn.DailyBurnInRangeAsync(user, weekStart, effectiveEnd, ct);
        var entries = await _consumptionEntries.ListForUserInRangeAsync(user.Id, weekStart, effectiveEnd, ct);
        var entryCountByDay = entries
            .Where(e => e.Skipped != true)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var weightHistory = await _weights.ListForUserAsync(user.Id, ct); // most-recent first
        (double Value, WeightUnit Unit)? latestWeight = weightHistory.Count > 0
            ? (weightHistory[0].Value, weightHistory[0].Unit)
            : null;
        var kindle = KindleCostCalculator.TryCompute(
            user.SexAtBirth, user.HeightCm, user.DateOfBirth, latestWeight, today);
        var kindleAvailable = kindle is not null;
        var kindlePerDay = kindle ?? 0;

        var schedule = WeightSchedule.FromUser(user);
        var weightDatesInWeek = weightHistory
            .Where(w => w.Date >= weekStart && w.Date <= effectiveEnd)
            .Select(w => w.Date)
            .ToHashSet();

        // The budget for adherence: a recurring calorie cap wins; otherwise direction from the
        // primary goal around the maintenance line (when kindle is known).
        var goals = await _goals.ListForUserAsync(user.Id, ct);
        var capGoal = goals.FirstOrDefault(g =>
            g.MetricType == GoalMetricType.Nutrient
            && string.Equals(g.NutrientId, CaloriesNutrientId, StringComparison.OrdinalIgnoreCase)
            && g.Cadence == GoalCadence.Recurring
            && !g.ExceedingIsPositive);

        var days = new List<ReviewDayDto>(daysCounted);
        for (var day = weekStart; day <= effectiveEnd; day = day.AddDays(1))
        {
            var fuel = fuelByDay.TryGetValue(day, out var f) ? Calories(f) : 0;
            var burn = burnByDay.TryGetValue(day, out var b) ? b : 0;
            var net = fuel - burn - kindlePerDay;
            var fuelEntries = entryCountByDay.TryGetValue(day, out var c) ? c : 0;
            var fullyLogged = fuelEntries >= GamificationAwardService.FullDayFuelEntries;
            var hasData = fuel > 0 || burn > 0 || fuelEntries > 0;

            var adherent = hasData && IsAdherent(user, capGoal, kindleAvailable, fuel, net, fullyLogged);
            var due = schedule?.IsDueOn(day) ?? false;
            var weighed = weightDatesInWeek.Contains(day);

            days.Add(new ReviewDayDto(day, fuel, burn, net, fuelEntries, fullyLogged, adherent, due, weighed));
        }

        var fullyLoggedDays = days.Count(d => d.FullyLogged);
        var adherentDays = days.Count(d => d.Adherent);
        var weighInsDue = days.Count(d => d.WeighInDue);
        var weighInsLogged = days.Count(d => d.WeighInDue && d.WeighInLogged);

        var loggingScore = (int)Math.Round(50.0 * fullyLoggedDays / daysCounted);
        var adherenceScore = (int)Math.Round(30.0 * adherentDays / daysCounted);
        var weightScore = weighInsDue == 0 ? 20 : (int)Math.Round(20.0 * weighInsLogged / weighInsDue);
        var score = Math.Clamp(loggingScore + adherenceScore + weightScore, 0, 100);

        // Best day: of the days with data, the one closest to its budget.
        var bestDay = days
            .Where(d => d.Fuel > 0 || d.Burn > 0 || d.FuelEntries > 0)
            .OrderBy(d => capGoal is not null ? Math.Max(0, d.Fuel - capGoal.TargetValue) : Math.Abs(d.Net))
            .ThenBy(d => d.Date)
            .Select(d => (DateOnly?)d.Date)
            .FirstOrDefault();

        var (delta, deltaUnit) = WeightDelta(weightHistory, weekStart, effectiveEnd);

        var ledger = stats?.DailyAwards ?? new List<DailyAward>();
        var pointsEarned = ledger
            .Where(a => a.Date >= weekStart && a.Date <= weekEnd)
            .Sum(a => (long)a.EntryPoints
                + (a.FullDayAwarded ? GamificationAwardService.FullDayPoints : 0)
                + (a.WeightAwarded ? GamificationAwardService.ScheduledWeighInPoints : 0));

        var completed = stats?.WeeklyReviews.FirstOrDefault(r => r.WeekStart == weekStart);

        var dto = new WeeklyReviewDto(
            weekStart, weekEnd,
            completed is not null, completed?.CompletedAt,
            completed?.Score ?? score,
            loggingScore, adherenceScore, weightScore,
            fullyLoggedDays, adherentDays, weighInsDue, weighInsLogged,
            days.Sum(d => d.Fuel), days.Sum(d => d.Burn), days.Sum(d => d.Net), kindleAvailable,
            days, bestDay, delta, deltaUnit,
            pointsEarned, completed?.PointsAwarded);

        return new WeekComputation(completed?.Score ?? score, dto);
    }

    private static bool IsAdherent(AppUser user, Goal? capGoal, bool kindleAvailable, double fuel, double net, bool fullyLogged)
    {
        if (capGoal is not null) return fuel <= capGoal.TargetValue;
        if (kindleAvailable)
        {
            return user.PrimaryGoal switch
            {
                GoalType.WeightLoss => net <= 0,
                GoalType.WeightGain => net >= 0,
                _ => Math.Abs(net) <= MaintainBandKcal,
            };
        }
        // No budget reference at all: engagement is the best proxy.
        return fullyLogged;
    }

    /// <summary>
    /// Weight change across the week: last in-week entry minus the baseline (the last entry at or
    /// before the week start, falling back to the first in-week entry). Expressed in the in-week
    /// entry's unit.
    /// </summary>
    private static (double? Delta, WeightUnit? Unit) WeightDelta(
        IReadOnlyList<WeightEntry> historyDesc, DateOnly weekStart, DateOnly effectiveEnd)
    {
        var lastInWeek = historyDesc.FirstOrDefault(w => w.Date >= weekStart && w.Date <= effectiveEnd);
        if (lastInWeek is null) return (null, null);

        var baseline = historyDesc.FirstOrDefault(w => w.Date < weekStart)
            ?? historyDesc.Where(w => w.Date >= weekStart && w.Date <= effectiveEnd).MinBy(w => w.Date);
        if (baseline is null || baseline == lastInWeek) return (null, null);

        var baselineValue = baseline.Unit == lastInWeek.Unit
            ? baseline.Value
            : baseline.Unit == WeightUnit.Kilogram
                ? baseline.Value * PoundsPerKilogram
                : baseline.Value / PoundsPerKilogram;

        return (Math.Round(lastInWeek.Value - baselineValue, 1), lastInWeek.Unit);
    }

    private static double Calories(IReadOnlyList<NutrientAmount> nutrition) =>
        nutrition.FirstOrDefault(a =>
            string.Equals(a.NutrientId, CaloriesNutrientId, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
}
