using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for weight tracking. Weight is strictly per-user data: every entry is owned by,
/// and only visible to, the recording user (there is no app-level or cross-user view, even for admins).
/// At most one entry exists per user per calendar day — <see cref="RecordAsync"/> upserts by date so a
/// second submission for the same day updates that day's entry rather than creating a duplicate.
/// </summary>
public sealed class WeightService
{
    private readonly IWeightEntryRepository _repo;
    private readonly GamificationAwardService _awards;

    public WeightService(IWeightEntryRepository repo, GamificationAwardService awards)
    {
        _repo = repo;
        _awards = awards;
    }

    /// <summary>The caller's own weight history, most recent day first.</summary>
    public async Task<IReadOnlyList<WeightEntryDto>> ListAsync(AppUser user, CancellationToken ct)
    {
        var entries = await _repo.ListForUserAsync(user.Id, ct);
        return entries.Select(ToDto).ToList();
    }

    /// <summary>
    /// Records the caller's weight for a day (defaulting to today). If an entry already exists for that
    /// day it is updated in place; otherwise a new one is created.
    /// </summary>
    public async Task<WeightEntryDto> RecordAsync(AppUser user, WeightEntryInput input, CancellationToken ct)
    {
        Validate(input);
        var date = input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var existing = await _repo.GetForUserAndDateAsync(user.Id, date, ct);
        if (existing is not null)
        {
            existing.Value = input.Value;
            existing.Unit = input.Unit;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _repo.UpdateAsync(existing, ct);
            // The award is once-per-due-date, so an update after the original log is a no-op.
            await _awards.TryAwardWeightLoggedAsync(user, date, ct);
            return ToDto(existing);
        }

        var entry = new WeightEntry
        {
            OwnerUserId = user.Id,
            Date = date,
            Value = input.Value,
            Unit = input.Unit,
        };
        await _repo.AddAsync(entry, ct);
        await _awards.TryAwardWeightLoggedAsync(user, date, ct);
        return ToDto(entry);
    }

    private static void Validate(WeightEntryInput input)
    {
        if (!(input.Value > 0))
            throw new ValidationException("Weight must be greater than zero.");
        if (input.Value > InputLimits.WeightValueMax)
            throw new ValidationException($"Weight must be {InputLimits.WeightValueMax} or less.");
        if (input.Date is { } date && date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Weight date cannot be in the future.");
    }

    private static WeightEntryDto ToDto(WeightEntry w) =>
        new(w.Id, w.Date, w.Value, w.Unit, w.CreatedAt, w.UpdatedAt);
}
