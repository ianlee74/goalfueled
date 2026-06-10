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

    public UserProfileService(IUserRepository users, WeightService weights)
    {
        _users = users;
        _weights = weights;
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
        ValidateWeightUnit(input.WeightUnit);

        // Record today's weight first (validates the value/unit and creates the WeightEntry the meal-plan
        // gate and weight goals read). If it throws, onboarding is not marked complete.
        await _weights.RecordAsync(user, new WeightEntryInput(input.CurrentWeight, input.WeightUnit), ct);

        user.PrimaryGoal = input.PrimaryGoal;
        user.SexAtBirth = input.SexAtBirth;
        user.HeightCm = input.HeightCm;
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

        if (input.CurrentWeight is { } weight)
        {
            ValidateWeightUnit(input.WeightUnit);
            await _weights.RecordAsync(user, new WeightEntryInput(weight, input.WeightUnit), ct);
        }

        user.PrimaryGoal = input.PrimaryGoal;
        user.SexAtBirth = input.SexAtBirth;
        user.HeightCm = input.HeightCm;
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

    private static void ValidateHeight(double heightCm)
    {
        if (!(heightCm > 0))
            throw new ValidationException("Height must be greater than zero.");
        if (heightCm > InputLimits.HeightCmMax)
            throw new ValidationException($"Height must be {InputLimits.HeightCmMax} cm or less.");
    }
}
