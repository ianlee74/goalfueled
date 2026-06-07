namespace GoalFueled.Api.Auth;

/// <summary>
/// A caller's identity as resolved from an identity provider, before it is mapped to a persisted
/// <see cref="Domain.Entities.AppUser"/>. Provider-agnostic: the dev provider and the future
/// social providers (Google/Microsoft/Facebook via Entra External ID) all produce this shape.
/// </summary>
/// <param name="Provider">Provider key, e.g. "dev", "google", "microsoft", "facebook".</param>
/// <param name="Subject">The provider's stable unique id for this user.</param>
/// <param name="Email">User email (used for display and as a soft account hint).</param>
/// <param name="DisplayName">Human-friendly name.</param>
/// <param name="AvatarUrl">Optional profile image URL.</param>
public sealed record AuthenticatedIdentity(
    string Provider,
    string Subject,
    string Email,
    string DisplayName,
    string? AvatarUrl = null);
