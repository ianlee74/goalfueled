using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Application logic for account sharing: an owner creates single-use invitation links granting a guest the
/// permission to view their log and/or manage their meal plans; a recipient accepts or rejects a link
/// (which spends it); and a guest who has accepted can later "act as" the owner. All authorization is
/// enforced here so the Function endpoints stay thin.
///
/// <para>A guest operating <em>as</em> someone else (via a share) may NOT create, manage or accept shares —
/// those actions always apply to the signed-in person's own account, so they are blocked while acting via a
/// share to avoid one share being used to spawn another.</para>
/// </summary>
public sealed class SharingService
{
    private readonly IShareInvitationRepository _shares;
    private readonly IUserRepository _users;

    public SharingService(IShareInvitationRepository shares, IUserRepository users)
    {
        _shares = shares;
        _users = users;
    }

    // ---- owner: create & manage invitations ----

    /// <summary>Creates a pending invitation the owner can send. ViewLog is always granted.</summary>
    public async Task<ShareInvitationDto> CreateAsync(AppUser owner, CreateShareInvitationInput input, CancellationToken ct)
    {
        EnsureNotActingViaShare(owner);
        InputLimits.LimitText(input.Label, InputLimits.NameMaxLength, "Label");

        var invitation = new ShareInvitation
        {
            OwnerUserId = owner.Id,
            Label = string.IsNullOrWhiteSpace(input.Label) ? null : input.Label.Trim(),
            Permissions = NormalizePermissions(input.Permissions),
        };

        await _shares.AddAsync(invitation, ct);
        return ToOwnerDto(invitation, guest: null);
    }

    /// <summary>The owner's invitations (pending and resolved), most recent first.</summary>
    public async Task<IReadOnlyList<ShareInvitationDto>> ListMineAsync(AppUser owner, CancellationToken ct)
    {
        EnsureNotActingViaShare(owner);
        var items = await _shares.ListByOwnerAsync(owner.Id, ct);

        // Resolve accepted guests' display info for the list (small N).
        var dtos = new List<ShareInvitationDto>(items.Count);
        foreach (var s in items)
        {
            var guest = s.GuestUserId is null ? null : await _users.GetByIdAsync(s.GuestUserId, ct);
            dtos.Add(ToOwnerDto(s, guest));
        }
        return dtos;
    }

    /// <summary>
    /// Revokes (deletes) an invitation the owner created. A still-pending link is deactivated so it can
    /// never be used; an already-accepted grant is revoked so the guest loses access. Already-rejected or
    /// already-deleted invitations are left as-is (idempotent). Owner only.
    /// </summary>
    public async Task DeleteAsync(AppUser owner, string id, CancellationToken ct)
    {
        EnsureNotActingViaShare(owner);
        var invitation = await _shares.GetByIdAsync(id, ct) ?? throw new NotFoundException("Invitation not found.");
        if (invitation.OwnerUserId != owner.Id)
            throw new NotFoundException("Invitation not found.");

        if (invitation.Status is ShareInvitationStatus.Pending or ShareInvitationStatus.Accepted)
        {
            invitation.Status = ShareInvitationStatus.Deleted;
            invitation.RespondedAt = DateTimeOffset.UtcNow;
            await _shares.UpdateAsync(invitation, ct);
        }
    }

    // ---- recipient: preview & respond ----

    /// <summary>
    /// What the recipient sees before deciding. Throws 404 for an unknown token. The viewer may be
    /// <c>null</c> when the link is opened by someone who has not signed in yet — the link token is itself
    /// the capability, so an anonymous viewer can still see who is inviting them and what they'd be granted
    /// (and decline). <see cref="ShareInvitationPreviewDto.CanRespond"/> is only true for a signed-in,
    /// eligible viewer (anonymous viewers must sign in before they can <em>accept</em>).
    /// </summary>
    public async Task<ShareInvitationPreviewDto> PreviewAsync(AppUser? viewer, string token, CancellationToken ct)
    {
        var invitation = await _shares.GetByTokenAsync(token, ct)
            ?? throw new NotFoundException("Invitation not found.");
        var owner = await _users.GetByIdAsync(invitation.OwnerUserId, ct);

        var isOwner = viewer is not null && invitation.OwnerUserId == viewer.Id;
        var canRespond = invitation.IsPending && !isOwner
            && viewer is not null && !viewer.IsActingViaShare;

        return new ShareInvitationPreviewDto(
            invitation.Status,
            ToPermissionList(invitation.Permissions),
            invitation.OwnerUserId,
            owner?.DisplayName ?? "A GoalFueled user",
            owner?.Email,
            canRespond,
            isOwner);
    }

    /// <summary>
    /// Accepts or rejects a pending invitation, spending the link. On accept the invitation becomes the
    /// guest's durable access grant — accepting therefore requires a signed-in viewer. Rejecting is allowed
    /// anonymously (a recipient may decline a link without ever creating an account; the token is the
    /// capability). The owner can't respond to their own invite; an already-spent link can't be reused; a
    /// guest currently acting via another share can't respond as that account.
    /// </summary>
    public async Task<ShareInvitationPreviewDto> RespondAsync(AppUser? viewer, string token, bool accept, CancellationToken ct)
    {
        if (viewer is not null) EnsureNotActingViaShare(viewer);
        if (accept && viewer is null)
            throw new UnauthorizedException("Sign in to accept an invitation.");

        var invitation = await _shares.GetByTokenAsync(token, ct)
            ?? throw new NotFoundException("Invitation not found.");

        if (viewer is not null && invitation.OwnerUserId == viewer.Id)
            throw new ValidationException("You can't accept your own invitation.");
        if (!invitation.IsPending)
            throw new ValidationException("This invitation has already been used.");

        invitation.Status = accept ? ShareInvitationStatus.Accepted : ShareInvitationStatus.Rejected;
        invitation.RespondedAt = DateTimeOffset.UtcNow;
        if (accept) invitation.GuestUserId = viewer!.Id;

        await _shares.UpdateAsync(invitation, ct);

        var owner = await _users.GetByIdAsync(invitation.OwnerUserId, ct);
        return new ShareInvitationPreviewDto(
            invitation.Status,
            ToPermissionList(invitation.Permissions),
            invitation.OwnerUserId,
            owner?.DisplayName ?? "A GoalFueled user",
            owner?.Email,
            CanRespond: false,
            IsOwner: false);
    }

    // ---- account switching ----

    /// <summary>
    /// The accounts the signed-in user may operate as: their own account first, then every owner who has an
    /// active share grant to them. Always resolved against the SIGNED-IN user, even while acting via a share
    /// (so the switcher still lists every account and lets the user switch back to themselves).
    /// </summary>
    public async Task<IReadOnlyList<SharedAccountDto>> ListSwitchableAccountsAsync(AppUser user, CancellationToken ct)
    {
        // The signed-in person's own id: when acting via a share, that's the guest id, not the owner's.
        var selfId = user.ActingViaShareGuestUserId ?? user.Id;
        var self = user.IsActingViaShare ? await _users.GetByIdAsync(selfId, ct) : user;

        var accounts = new List<SharedAccountDto>();
        if (self is not null)
        {
            accounts.Add(new SharedAccountDto(
                self.Id, self.DisplayName, self.Email, self.AvatarUrl, IsSelf: true,
                ToPermissionList(SharePermission.ViewLog | SharePermission.ManageMealPlans)));
        }

        foreach (var grant in await _shares.ListActiveGrantsForGuestAsync(selfId, ct))
        {
            var owner = await _users.GetByIdAsync(grant.OwnerUserId, ct);
            if (owner is null) continue;
            accounts.Add(new SharedAccountDto(
                owner.Id, owner.DisplayName, owner.Email, owner.AvatarUrl, IsSelf: false,
                ToPermissionList(grant.Permissions)));
        }

        return accounts;
    }

    // ---- helpers ----

    /// <summary>Collapses the requested permissions into a flags set, always forcing ViewLog on.</summary>
    private static SharePermission NormalizePermissions(IReadOnlyList<SharePermission>? requested)
    {
        var result = SharePermission.ViewLog; // every share at least lets the guest view the log
        foreach (var p in requested ?? Array.Empty<SharePermission>())
        {
            if (Enum.IsDefined(typeof(SharePermission), p)) result |= p;
        }
        return result;
    }

    /// <summary>Expands a flags set into the list of individual granted permissions (ViewLog, ManageMealPlans).</summary>
    private static IReadOnlyList<SharePermission> ToPermissionList(SharePermission permissions)
    {
        var list = new List<SharePermission>();
        if (permissions.HasFlag(SharePermission.ViewLog)) list.Add(SharePermission.ViewLog);
        if (permissions.HasFlag(SharePermission.ManageMealPlans)) list.Add(SharePermission.ManageMealPlans);
        return list;
    }

    private static ShareInvitationDto ToOwnerDto(ShareInvitation s, AppUser? guest) => new(
        s.Id,
        s.Status,
        ToPermissionList(s.Permissions),
        s.Label,
        // The token (and thus the shareable link) is only meaningful while the invitation is still usable.
        s.IsPending ? s.Token : null,
        s.IsPending ? $"/invite/{s.Token}" : null,
        s.GuestUserId,
        guest?.DisplayName,
        guest?.Email,
        s.CreatedAt,
        s.RespondedAt);

    /// <summary>Sharing actions always operate on the signed-in user's own account, never a shared one.</summary>
    private static void EnsureNotActingViaShare(AppUser user)
    {
        if (user.IsActingViaShare)
            throw new ForbiddenException("Switch back to your own account to manage sharing.");
    }
}
