using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Services;

/// <summary>
/// Grants Fuel Points for logging behaviors (DESIGN.md §4.2). Hooked from the consumption, burn,
/// and weight services after a successful write. Awards are behavior-only (never tied to deficit
/// size or weight lost), accrue-only (no clawbacks when entries are deleted), and idempotent per
/// day via the rolling <see cref="UserStats.DailyAwards"/> ledger.
///
/// <para>All public entry points swallow and log failures: a stats hiccup must never fail the
/// user's actual log write, which already succeeded.</para>
/// </summary>
public sealed class GamificationAwardService
{
    /// <summary>FP per individual fuel/burn entry.</summary>
    public const int EntryPoints = 10;

    /// <summary>Max FP per day from individual entries.</summary>
    public const int DailyEntryPointsCap = 50;

    /// <summary>FP for a "full day" — at least <see cref="FullDayFuelEntries"/> fuel entries logged.</summary>
    public const int FullDayPoints = 25;

    public const int FullDayFuelEntries = 2;

    /// <summary>FP for logging weight on a scheduled due day.</summary>
    public const int ScheduledWeighInPoints = 50;

    /// <summary>Days of per-day award bookkeeping kept on the document.</summary>
    private const int LedgerWindowDays = 35;

    private readonly IUserStatsRepository _stats;
    private readonly IConsumptionEntryRepository _consumption;
    private readonly ILogger<GamificationAwardService> _logger;

    public GamificationAwardService(
        IUserStatsRepository stats,
        IConsumptionEntryRepository consumption,
        ILogger<GamificationAwardService> logger)
    {
        _stats = stats;
        _consumption = consumption;
        _logger = logger;
    }

    /// <summary>Awards entry FP for a logged fuel item, plus the full-day bonus once ≥2 fuel entries exist for the date.</summary>
    public Task TryAwardFuelLoggedAsync(AppUser user, DateOnly date, CancellationToken ct) =>
        Guarded(user, async () =>
        {
            var (stats, isNew) = await GetOrCreateAsync(user, ct);
            var day = AwardFor(stats, date);

            GrantEntryPoints(stats, day);

            if (!day.FullDayAwarded)
            {
                // Includes the entry that triggered this hook (it was persisted before the hook ran).
                var fuelEntries = (await _consumption.ListForUserAndDateAsync(user.Id, date, ct))
                    .Count(e => e.Skipped != true);
                if (fuelEntries >= FullDayFuelEntries)
                {
                    day.FullDayAwarded = true;
                    stats.FuelPoints += FullDayPoints;
                }
            }

            await SaveAsync(stats, isNew, ct);
        });

    /// <summary>Awards entry FP for a logged burn activity.</summary>
    public Task TryAwardBurnLoggedAsync(AppUser user, DateOnly date, CancellationToken ct) =>
        Guarded(user, async () =>
        {
            var (stats, isNew) = await GetOrCreateAsync(user, ct);
            GrantEntryPoints(stats, AwardFor(stats, date));
            await SaveAsync(stats, isNew, ct);
        });

    /// <summary>Awards the weigh-in bonus when a weight was logged on a day the user's schedule made it due.</summary>
    public Task TryAwardWeightLoggedAsync(AppUser user, DateOnly date, CancellationToken ct) =>
        Guarded(user, async () =>
        {
            var schedule = WeightSchedule.FromUser(user);
            if (schedule is null || !schedule.IsDueOn(date)) return;

            var (stats, isNew) = await GetOrCreateAsync(user, ct);
            var day = AwardFor(stats, date);
            if (day.WeightAwarded) return;

            day.WeightAwarded = true;
            stats.FuelPoints += ScheduledWeighInPoints;
            await SaveAsync(stats, isNew, ct);
        });

    private static void GrantEntryPoints(UserStats stats, DailyAward day)
    {
        if (day.EntryPoints >= DailyEntryPointsCap) return;
        day.EntryPoints += EntryPoints;
        stats.FuelPoints += EntryPoints;
    }

    private static DailyAward AwardFor(UserStats stats, DateOnly date)
    {
        var day = stats.DailyAwards.FirstOrDefault(a => a.Date == date);
        if (day is null)
        {
            day = new DailyAward { Date = date };
            stats.DailyAwards.Add(day);
        }
        return day;
    }

    private async Task<(UserStats Stats, bool IsNew)> GetOrCreateAsync(AppUser user, CancellationToken ct)
    {
        var stats = await _stats.GetForUserAsync(user.Id, ct);
        if (stats is not null) return (stats, false);
        return (new UserStats { OwnerUserId = user.Id }, true);
    }

    private async Task SaveAsync(UserStats stats, bool isNew, CancellationToken ct)
    {
        // Prune bookkeeping outside the rolling window so the document stays small.
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-LedgerWindowDays);
        stats.DailyAwards.RemoveAll(a => a.Date < cutoff);
        stats.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew) await _stats.AddAsync(stats, ct);
        else await _stats.UpdateAsync(stats, ct);
    }

    private async Task Guarded(AppUser user, Func<Task> award)
    {
        try
        {
            await award();
        }
        catch (Exception ex)
        {
            // Never fail the user's log write over a stats problem; the points can be re-earned.
            _logger.LogWarning(ex, "Fuel point award failed for user {UserId}.", user.Id);
        }
    }
}
