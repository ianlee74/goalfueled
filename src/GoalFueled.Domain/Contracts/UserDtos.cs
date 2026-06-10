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
    bool HasCompletedOnboarding);

/// <summary>
/// Payload for completing the first-login welcome flow. Captures the basic profile info gathered
/// the one time on first login: goal, sex at birth, height, and current weight. The current weight
/// (value + unit) is recorded as today's <see cref="WeightEntryDto"/> via the existing weight feature.
/// </summary>
public sealed record OnboardingInput(
    GoalType PrimaryGoal,
    SexAtBirth SexAtBirth,
    double HeightCm,
    double CurrentWeight,
    WeightUnit WeightUnit);

/// <summary>
/// Payload for updating the editable profile attributes later (the Profile page). Goal, sex, and
/// height are always present; current weight is optional — when provided it upserts today's weight entry.
/// </summary>
public sealed record ProfileInput(
    GoalType PrimaryGoal,
    SexAtBirth SexAtBirth,
    double HeightCm,
    double? CurrentWeight,
    WeightUnit WeightUnit);

/// <summary>Payload for an admin toggling their admin-view on or off.</summary>
public sealed record AdminViewInput(bool Enabled);

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
