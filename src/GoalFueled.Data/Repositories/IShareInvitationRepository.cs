using GoalFueled.Domain.Entities;

namespace GoalFueled.Data.Repositories;

public interface IShareInvitationRepository
{
    Task<ShareInvitation?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Finds an invitation by the opaque token embedded in its link (the recipient's entry point).</summary>
    Task<ShareInvitation?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Every invitation an owner has created, most recent first (the "manage invitations" list).</summary>
    Task<IReadOnlyList<ShareInvitation>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>The active accepted grants a guest holds — the owners whose accounts the guest may act as.</summary>
    Task<IReadOnlyList<ShareInvitation>> ListActiveGrantsForGuestAsync(string guestUserId, CancellationToken ct = default);

    /// <summary>The active accepted grant for a specific (owner, guest) pair, or null if there is none.</summary>
    Task<ShareInvitation?> GetActiveGrantAsync(string ownerUserId, string guestUserId, CancellationToken ct = default);

    Task<ShareInvitation> AddAsync(ShareInvitation invitation, CancellationToken ct = default);

    Task UpdateAsync(ShareInvitation invitation, CancellationToken ct = default);

    Task DeleteAsync(ShareInvitation invitation, CancellationToken ct = default);

    /// <summary>
    /// Every invitation that references the given user as either its owner or its accepted guest. Used to
    /// purge a user's shares when their account is deleted (so no dangling grants survive).
    /// </summary>
    Task<IReadOnlyList<ShareInvitation>> ListTouchingUserAsync(string userId, CancellationToken ct = default);
}
