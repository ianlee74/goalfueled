using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for the current user's own profile: the one-time first-login onboarding capture
/// and later edits via the Profile page. Basic body info (sex at birth, height, primary goal) lives on
/// the <see cref="AppUser"/>; the captured "current weight" is recorded as today's <see cref="WeightEntry"/>
/// through the existing <see cref="WeightService"/> so the meal-plan weight gate and weight goals are
/// satisfied by a single source of truth (weight is never duplicated onto the profile).
/// </summary>
public sealed class UserProfileService
{
    private readonly IUserRepository _users;
    private readonly WeightService _weights;
    private readonly MealPlanService _plans;

    public UserProfileService(IUserRepository users, WeightService weights, MealPlanService plans)
    {
        _users = users;
        _weights = weights;
        _plans = plans;
    }

    /// <summary>
    /// Completes the first-login welcome flow, persisting all captured basic info and recording the
    /// supplied current weight as today's entry. <see cref="AppUser.HasCompletedOnboarding"/> is only
    /// set once every required field validates and persists.
    /// </summary>
    public async Task<AppUser> CompleteOnboardingAsync(AppUser user, OnboardingInput input, CancellationToken ct)
    {
        ValidateGoal(input.PrimaryGoal);
        ValidateSex(input.SexAtBirth);
        ValidateHeight(input.HeightCm);
        ValidateDateOfBirth(input.DateOfBirth);
        ValidateWeightUnit(input.WeightUnit);

        // Record today's weight first (validates the value/unit and creates the WeightEntry the meal-plan
        // gate and weight goals read). If it throws, onboarding is not marked complete.
        await _weights.RecordAsync(user, new WeightEntryInput(input.CurrentWeight, input.WeightUnit), ct);

        ValidateUnitsPreference(input.UnitsPreference);

        user.PrimaryGoal = input.PrimaryGoal;
        user.SexAtBirth = input.SexAtBirth;
        user.HeightCm = input.HeightCm;
        user.DateOfBirth = input.DateOfBirth;
        user.UnitsPreference = input.UnitsPreference;
        user.HasCompletedOnboarding = true;
        await _users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>
    /// Updates the editable profile attributes later. Goal, sex, and height are always written; a current
    /// weight is recorded (upserting today's entry) only when supplied.
    /// </summary>
    public async Task<AppUser> UpdateProfileAsync(AppUser user, ProfileInput input, CancellationToken ct)
    {
        ValidateGoal(input.PrimaryGoal);
        ValidateSex(input.SexAtBirth);
        ValidateHeight(input.HeightCm);
        ValidateDateOfBirth(input.DateOfBirth);

        if (input.CurrentWeight is { } weight)
        {
            ValidateWeightUnit(input.WeightUnit);
            await _weights.RecordAsync(user, new WeightEntryInput(weight, input.WeightUnit), ct);
        }

        ValidateUnitsPreference(input.UnitsPreference);

        user.PrimaryGoal = input.PrimaryGoal;
        user.SexAtBirth = input.SexAtBirth;
        user.HeightCm = input.HeightCm;
        user.DateOfBirth = input.DateOfBirth;
        user.UnitsPreference = input.UnitsPreference;
        await _users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>
    /// Chooses (or clears) the meal plan the user is currently following — the one The Log follows along
    /// with. A null/blank id clears the selection; a non-blank id must reference a plan the user can view.
    /// </summary>
    public async Task<AppUser> SetActiveMealPlanAsync(AppUser user, ActiveMealPlanInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.MealPlanId))
        {
            user.ActiveMealPlanId = null;
        }
        else
        {
            // GetAsync enforces visibility and throws NotFound if the plan is missing/not the user's.
            var plan = await _plans.GetAsync(user, input.MealPlanId, ct);
            user.ActiveMealPlanId = plan.Id;
        }

        await _users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>
    /// Toggles an admin's "admin view" on or off so they can browse the app as a regular user.
    /// Authorization is enforced here: only a genuine admin (<see cref="AppUser.IsAdmin"/>) may change the
    /// setting — a non-admin can NEVER enable it, so the toggle can never grant real privileges it didn't
    /// already have. The persisted <see cref="AppUser.AdminViewEnabled"/> drives
    /// <see cref="AppUser.IsActingAsAdmin"/>, which every admin-only check reads.
    /// </summary>
    public async Task<AppUser> SetAdminViewAsync(AppUser user, AdminViewInput input, CancellationToken ct)
    {
        if (!user.IsAdmin)
            throw new ForbiddenException("Only admins can change the admin view.");

        user.AdminViewEnabled = input.Enabled;
        await _users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>
    /// Sets (or clears) the user's recurring weight-measurement schedule. A null cadence clears it (no
    /// recurring weight task then appears in The Log). When a cadence is given, the start date defaults to
    /// today; for the "every N" cadences the interval must be ≥ 1 and within a sane bound.
    /// </summary>
    public async Task<AppUser> SetWeightScheduleAsync(AppUser user, WeightScheduleInput input, CancellationToken ct)
    {
        if (input.Cadence is not { } cadence)
        {
            user.WeightScheduleCadence = null;
            user.WeightScheduleInterval = null;
            user.WeightScheduleStartDate = null;
            await _users.UpdateAsync(user, ct);
            return user;
        }

        if (!Enum.IsDefined(typeof(WeightScheduleCadence), cadence))
            throw new ValidationException("Unknown weight-measurement cadence.");

        var interval = cadence is WeightScheduleCadence.EveryNDays or WeightScheduleCadence.EveryNWeeks
            ? input.Interval
            : 1;
        if (cadence is WeightScheduleCadence.EveryNDays or WeightScheduleCadence.EveryNWeeks)
        {
            if (interval < 1)
                throw new ValidationException("The interval must be at least 1.");
            if (interval > InputLimits.WeightScheduleIntervalMax)
                throw new ValidationException($"The interval must be {InputLimits.WeightScheduleIntervalMax} or less.");
        }

        user.WeightScheduleCadence = cadence;
        user.WeightScheduleInterval = interval;
        user.WeightScheduleStartDate = input.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await _users.UpdateAsync(user, ct);
        return user;
    }

    private static void ValidateGoal(GoalType goal)
    {
        if (!Enum.IsDefined(typeof(GoalType), goal))
            throw new ValidationException("Unknown goal type.");
    }

    private static void ValidateSex(SexAtBirth sex)
    {
        if (!Enum.IsDefined(typeof(SexAtBirth), sex))
            throw new ValidationException("Sex at birth is required.");
    }

    private static void ValidateWeightUnit(WeightUnit unit)
    {
        if (!Enum.IsDefined(typeof(WeightUnit), unit))
            throw new ValidationException("Unknown weight unit.");
    }

    private static void ValidateUnitsPreference(UnitsPreference preference)
    {
        if (!Enum.IsDefined(typeof(UnitsPreference), preference))
            throw new ValidationException("Unknown units preference.");
    }

    private static void ValidateHeight(double heightCm)
    {
        if (!(heightCm > 0))
            throw new ValidationException("Height must be greater than zero.");
        if (heightCm > InputLimits.HeightCmMax)
            throw new ValidationException($"Height must be {InputLimits.HeightCmMax} cm or less.");
    }

    /// <summary>
    /// Validates the date of birth: not in the future, and within a plausible age range (no older than
    /// <see cref="KindleLimits.MaxAgeYears"/>). Age is derived from the DOB and today's date, so we store the
    /// durable date rather than a snapshot age.
    /// </summary>
    private static void ValidateDateOfBirth(DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateOfBirth > today)
            throw new ValidationException("Date of birth cannot be in the future.");
        if (KindleCostCalculator.AgeOn(dateOfBirth, today) > KindleLimits.MaxAgeYears)
            throw new ValidationException($"Date of birth implies an age over {KindleLimits.MaxAgeYears} years.");
    }
}
