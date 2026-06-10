namespace GoalFueled.Domain.Contracts;

/// <summary>The signed-in user's profile and effective permissions, returned from /api/me.</summary>
public sealed record CurrentUserDto(
    string Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<AppRole> Roles,
    bool IsAdmin,
    bool AdminViewEnabled,
    bool IsActingAsAdmin,
    GoalType? PrimaryGoal,
    SexAtBirth? SexAtBirth,
    double? HeightCm,
    /// <summary>The user's date of birth (drives the Kindle BMR calc); null until provided.</summary>
    DateOnly? DateOfBirth,
    bool HasCompletedOnboarding,
    /// <summary>The meal plan the user is currently following (drives The Log); null if none chosen.</summary>
    string? ActiveMealPlanId,
    /// <summary>The user's recurring weight-measurement schedule, or null when none is configured.</summary>
    WeightScheduleDto? WeightSchedule,
    /// <summary>The user's preferred weight/height unit system (Metric = kg/cm, UsImperial = lb/ft-in).</summary>
    UnitsPreference UnitsPreference = UnitsPreference.UsImperial);

/// <summary>
/// A user's recurring weight-measurement schedule as returned to clients. Drives the recurring
/// "Log your weight" task in The Log. The start date's day-of-week is the chosen "day to start
/// measurements"; <see cref="Interval"/> is the N for the "every N" cadences (1 otherwise).
/// </summary>
public sealed record WeightScheduleDto(
    WeightScheduleCadence Cadence,
    /// <summary>The schedule's anchor date, serialized "yyyy-MM-dd".</summary>
    DateOnly StartDate,
    int Interval,
    /// <summary>Convenience copy of the start date's day-of-week (the chosen start day).</summary>
    DayOfWeek StartDayOfWeek);

/// <summary>
/// Payload for setting (or clearing) the user's weight-measurement schedule. A null <see cref="Cadence"/>
/// clears the schedule. <see cref="Interval"/> is required (≥ 1) for the "every N" cadences and ignored
/// otherwise; <see cref="StartDate"/> supplies the start day-of-week and recurrence anchor.
/// </summary>
public sealed record WeightScheduleInput(
    WeightScheduleCadence? Cadence,
    DateOnly? StartDate = null,
    int Interval = 1);

/// <summary>
/// Payload for completing the first-login welcome flow. Captures the basic profile info gathered
/// the one time on first login: goal, sex at birth, height, and current weight. The current weight
/// (value + unit) is recorded as today's <see cref="WeightEntryDto"/> via the existing weight feature.
/// </summary>
public sealed record OnboardingInput(
    GoalType PrimaryGoal,
    SexAtBirth SexAtBirth,
    double HeightCm,
    /// <summary>The user's date of birth, "yyyy-MM-dd" (required; drives the Kindle BMR calc).</summary>
    DateOnly DateOfBirth,
    double CurrentWeight,
    WeightUnit WeightUnit,
    /// <summary>Preferred weight/height unit system. Trailing with a default so positional callers don't break.</summary>
    UnitsPreference UnitsPreference = UnitsPreference.UsImperial);

/// <summary>
/// Payload for updating the editable profile attributes later (the Profile page). Goal, sex, and
/// height are always present; current weight is optional — when provided it upserts today's weight entry.
/// </summary>
public sealed record ProfileInput(
    GoalType PrimaryGoal,
    SexAtBirth SexAtBirth,
    double HeightCm,
    /// <summary>The user's date of birth, "yyyy-MM-dd" (required; drives the Kindle BMR calc).</summary>
    DateOnly DateOfBirth,
    double? CurrentWeight,
    WeightUnit WeightUnit,
    /// <summary>Preferred weight/height unit system. Trailing with a default so positional callers don't break.</summary>
    UnitsPreference UnitsPreference = UnitsPreference.UsImperial);

/// <summary>Payload for an admin toggling their admin-view on or off.</summary>
public sealed record AdminViewInput(bool Enabled);

/// <summary>
/// Payload for choosing (or clearing) the meal plan the user is currently following. A null/blank
/// <see cref="MealPlanId"/> clears the selection so The Log reverts to a plain free-form log.
/// </summary>
public sealed record ActiveMealPlanInput(string? MealPlanId);

/// <summary>A user as shown on the admin user-management screen.</summary>
public sealed record AdminUserDto(
    string Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<AppRole> Roles,
    bool IsAdmin,
    bool IsSelf);

/// <summary>Payload for an admin promoting/demoting a user's App Admin role.</summary>
public sealed record SetAdminRoleInput(bool IsAdmin);
