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
    bool HasCompletedOnboarding);

/// <summary>Payload for completing the first-login welcome flow.</summary>
public sealed record OnboardingInput(GoalType PrimaryGoal);

/// <summary>Payload for an admin toggling their admin-view on or off.</summary>
public sealed record AdminViewInput(bool Enabled);
