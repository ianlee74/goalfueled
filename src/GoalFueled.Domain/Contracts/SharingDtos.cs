namespace GoalFueled.Domain.Contracts;

/// <summary>
/// An invitation as shown to its OWNER on the "manage invitations" screen: its status, the permissions it
/// grants, who accepted it (if anyone), and the relative link path the owner can copy and send. The raw
/// <see cref="Token"/> is included only for pending invitations so the owner can (re)share the link.
/// </summary>
public sealed record ShareInvitationDto(
    string Id,
    ShareInvitationStatus Status,
    IReadOnlyList<SharePermission> Permissions,
    string? Label,
    /// <summary>Token for the link, present only while the invitation is still pending (and thus usable).</summary>
    string? Token,
    /// <summary>App-relative invite path "/invite/{token}", present only while pending.</summary>
    string? InvitePath,
    /// <summary>The guest who accepted, when accepted; null otherwise.</summary>
    string? GuestUserId,
    string? GuestDisplayName,
    string? GuestEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt);

/// <summary>Payload for creating a sharing invitation: a label plus the permissions to grant.</summary>
public sealed record CreateShareInvitationInput(
    /// <summary>Optional free-text label (e.g. "Dr. Smith"). Trimmed server-side.</summary>
    string? Label,
    /// <summary>The permissions to grant. ViewLog is implied/forced on; ManageMealPlans is optional.</summary>
    IReadOnlyList<SharePermission> Permissions);

/// <summary>
/// What the RECIPIENT sees when they open an invitation link: enough to decide whether to accept. Never
/// exposes the token back. <see cref="CanRespond"/> is false when the link is already spent (the recipient
/// is then shown the resolved status), or when the viewer is the owner (you can't accept your own invite).
/// </summary>
public sealed record ShareInvitationPreviewDto(
    ShareInvitationStatus Status,
    IReadOnlyList<SharePermission> Permissions,
    string OwnerUserId,
    string OwnerDisplayName,
    string? OwnerEmail,
    bool CanRespond,
    /// <summary>True when the viewer is the invitation's own owner (can't accept their own link).</summary>
    bool IsOwner);

/// <summary>
/// An account the signed-in user can switch into: their own, plus every owner who shared with them. The
/// account switcher renders this list; <see cref="IsSelf"/> marks the user's own account.
/// </summary>
public sealed record SharedAccountDto(
    string UserId,
    string DisplayName,
    string? Email,
    string? AvatarUrl,
    bool IsSelf,
    /// <summary>The permissions the user holds over this account; the full set for their own account.</summary>
    IReadOnlyList<SharePermission> Permissions);
