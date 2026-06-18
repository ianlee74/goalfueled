namespace GoalFueled.Domain.Entities;

/// <summary>
/// An invitation one user (the <see cref="OwnerUserId"/>) creates to let another person view and/or
/// manage their account. The owner sends the recipient a link carrying the opaque <see cref="Token"/>;
/// when the recipient signs in and opens it they may accept or reject it. On accept the invitation also
/// becomes the durable access <em>grant</em> — it records the <see cref="GuestUserId"/> who accepted and
/// the <see cref="Permissions"/> they hold, which account-switching reads to "act as" the owner.
///
/// <para>An invitation is single-use: a pending link can be accepted, rejected, or revoked exactly once,
/// after which it can never be reused (the recipient is shown the spent state). The owner can also revoke
/// a still-active accepted grant, which deactivates the guest's access.</para>
/// </summary>
public class ShareInvitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The account being shared (the link's creator).</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>
    /// The opaque, unguessable token embedded in the link the owner sends. Looked up directly when the
    /// recipient opens the link. Distinct from <see cref="Id"/> so the lookup key isn't the document id.
    /// </summary>
    public string Token { get; set; } = NewToken();

    /// <summary>
    /// Permissions the guest is granted, stored as the comma-joined string names of a
    /// <see cref="SharePermission"/> flags set (Cosmos can't map a flags enum directly; mirrors how
    /// <see cref="AppUser.Roles"/> is persisted). <see cref="SharePermission.ViewLog"/> is always included.
    /// </summary>
    public SharePermission Permissions { get; set; } = SharePermission.ViewLog;

    public ShareInvitationStatus Status { get; set; } = ShareInvitationStatus.Pending;

    /// <summary>
    /// The user who accepted the invitation; null while pending/rejected/revoked-before-accept. Once set,
    /// an <see cref="Status.Accepted"/> invitation is the access grant for this guest over the owner.
    /// </summary>
    public string? GuestUserId { get; set; }

    /// <summary>An optional free-text label the owner gives the invitation (e.g. "Dr. Smith").</summary>
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the invitation last changed status (accepted/rejected/revoked); null while pending.</summary>
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>True only while the link can still be accepted or rejected by a recipient.</summary>
    public bool IsPending => Status == ShareInvitationStatus.Pending;

    /// <summary>True when this invitation is an active access grant the guest can use to act as the owner.</summary>
    public bool IsActiveGrant => Status == ShareInvitationStatus.Accepted && GuestUserId is not null;

    /// <summary>A URL-safe random token for an invitation link.</summary>
    public static string NewToken() =>
        Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
}
