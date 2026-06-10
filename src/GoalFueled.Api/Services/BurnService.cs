using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for the daily <em>burn</em> log — exercise/activity that uses calories, the
/// counterpart to the consumption (fuel) log. Burn is strictly per-user data: every activity is owned by,
/// and only visible to, the logging user (no app-level or cross-user view, even for admins, mirroring
/// <see cref="ConsumptionService"/> / <see cref="WeightService"/>).
///
/// <para><b>Sign convention:</b> calories are stored and returned as a positive magnitude
/// (<see cref="BurnActivity.CaloriesBurned"/>). The negative sign is applied only by callers that combine
/// burn into a net-calorie summary (net = fuel − burn); this service never stores or returns a negative
/// value. The day view's <see cref="BurnDayDto.TotalCaloriesBurned"/> is therefore a positive total the
/// client subtracts.</para>
/// </summary>
public sealed class BurnService
{
    private readonly IBurnActivityRepository _repo;
    private readonly IWeightEntryRepository _weights;

    public BurnService(IBurnActivityRepository repo, IWeightEntryRepository weights)
    {
        _repo = repo;
        _weights = weights;
    }

    /// <summary>
    /// The caller's burn log for a day (defaulting to today) with the day's total calories burned and the
    /// derived passive "Kindle" cost (sedentary calorie usage). The Kindle cost is computed on the fly from
    /// the user's profile + latest recorded weight (never persisted) and is null when the profile is
    /// incomplete (so the client prompts the user rather than showing a wrong number).
    /// </summary>
    public async Task<BurnDayDto> GetDayAsync(AppUser user, DateOnly? date, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var activities = await _repo.ListForUserAndDateAsync(user.Id, day, ct);
        var dtos = activities.Select(ToDto).ToList();
        var total = dtos.Sum(a => a.CaloriesBurned);
        var kindle = await ComputeKindleCostAsync(user, ct);
        return new BurnDayDto(day, dtos, total, kindle);
    }

    /// <summary>
    /// The user's daily Kindle cost (Mifflin–St Jeor BMR × 1.2), or null when any required input is missing
    /// (no sex, no height, no date of birth, or no recorded weight). The latest recorded weight is the user's
    /// current weight; the calculation always uses it so the Kindle cost tracks weight changes without
    /// persisting a daily row. Age is derived from the date of birth as of today.
    /// </summary>
    private async Task<int?> ComputeKindleCostAsync(AppUser user, CancellationToken ct)
    {
        var history = await _weights.ListForUserAsync(user.Id, ct); // most-recent day first
        var latest = history.Count > 0
            ? ((double Value, WeightUnit Unit)?)(history[0].Value, history[0].Unit)
            : null;

        return KindleCostCalculator.TryCompute(
            user.SexAtBirth, user.HeightCm, user.DateOfBirth, latest,
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>Logs a burn activity for the caller (a new entry is always appended — no per-day upsert).</summary>
    public async Task<BurnActivityDto> LogAsync(AppUser user, BurnActivityInput input, CancellationToken ct)
    {
        Validate(input);
        var date = input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var activity = new BurnActivity
        {
            OwnerUserId = user.Id,
            Date = date,
            Time = input.Time,
            Name = input.Name.Trim(),
            CaloriesBurned = input.CaloriesBurned,
        };
        await _repo.AddAsync(activity, ct);
        return ToDto(activity);
    }

    /// <summary>
    /// The distinct activity names the caller has logged before, each with the calories from its most-recent
    /// entry — for the burn-name autocomplete. Ordered most-recent-first and de-duplicated by a
    /// case-insensitive name comparison (the first, i.e. most recent, entry for each name wins, so its
    /// calories become the default pre-fill).
    /// </summary>
    public async Task<IReadOnlyList<BurnSuggestionDto>> GetSuggestionsAsync(AppUser user, CancellationToken ct)
    {
        // Most-recent-first across the user's whole history (the repository orders by CreatedAt desc).
        var history = await _repo.ListForUserAsync(user.Id, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<BurnSuggestionDto>();
        foreach (var activity in history)
        {
            if (seen.Add(activity.Name))
                suggestions.Add(new BurnSuggestionDto(activity.Name, activity.CaloriesBurned));
        }
        return suggestions;
    }

    /// <summary>
    /// The caller's total calories burned per calendar day over the inclusive range
    /// <paramref name="from"/>..<paramref name="to"/>, keyed by date (positive magnitudes). Only days with
    /// at least one logged activity appear. Used by net-calorie roll-ups (net = fuel − burn).
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, int>> DailyBurnInRangeAsync(
        AppUser user, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var activities = await _repo.ListForUserInRangeAsync(user.Id, from, to, ct);
        return activities
            .GroupBy(a => a.Date)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.CaloriesBurned));
    }

    /// <summary>Removes one of the caller's logged burn activities.</summary>
    public async Task RemoveAsync(AppUser user, string id, CancellationToken ct)
    {
        var activity = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Burn activity not found.");
        // Activities are entirely private; not even admins get a cross-user view.
        if (activity.OwnerUserId != user.Id) throw new NotFoundException("Burn activity not found.");
        await _repo.DeleteAsync(activity, ct);
    }

    private static void Validate(BurnActivityInput input)
    {
        InputLimits.RequireText(input.Name, InputLimits.NameMaxLength, "Name");
        if (input.CaloriesBurned <= 0)
            throw new ValidationException("Calories burned must be greater than zero.");
        if (input.CaloriesBurned > InputLimits.BurnCaloriesMax)
            throw new ValidationException($"Calories burned must be {InputLimits.BurnCaloriesMax} or less.");
        if (input.Date is { } date && date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Burn date cannot be in the future.");
    }

    private static BurnActivityDto ToDto(BurnActivity a) =>
        new(a.Id, a.Date, a.Name, a.CaloriesBurned, a.CreatedAt, a.UpdatedAt, a.Time);
}
