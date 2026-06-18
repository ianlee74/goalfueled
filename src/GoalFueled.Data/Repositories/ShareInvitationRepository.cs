using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class ShareInvitationRepository : IShareInvitationRepository
{
    private readonly GoalFueledDbContext _db;

    public ShareInvitationRepository(GoalFueledDbContext db) => _db = db;

    public Task<ShareInvitation?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.ShareInvitations.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<ShareInvitation?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        // Token is a translatable string equality, but the Permissions flags column goes through a value
        // converter; materializing the (small) matching set avoids any provider translation surprises.
        var matches = await _db.ShareInvitations.Where(s => s.Token == token).ToListAsync(ct);
        return matches.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ShareInvitation>> ListByOwnerAsync(
        string ownerUserId, CancellationToken ct = default)
    {
        var items = await _db.ShareInvitations.Where(s => s.OwnerUserId == ownerUserId).ToListAsync(ct);
        return items.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<ShareInvitation>> ListActiveGrantsForGuestAsync(
        string guestUserId, CancellationToken ct = default)
    {
        // GuestUserId equality is translatable; the Accepted status filter (an enum) runs in memory.
        var items = await _db.ShareInvitations.Where(s => s.GuestUserId == guestUserId).ToListAsync(ct);
        return items
            .Where(s => s.IsActiveGrant)
            .OrderByDescending(s => s.RespondedAt ?? s.CreatedAt)
            .ToList();
    }

    public async Task<ShareInvitation?> GetActiveGrantAsync(
        string ownerUserId, string guestUserId, CancellationToken ct = default)
    {
        var items = await _db.ShareInvitations
            .Where(s => s.OwnerUserId == ownerUserId && s.GuestUserId == guestUserId)
            .ToListAsync(ct);
        return items.FirstOrDefault(s => s.IsActiveGrant);
    }

    public async Task<ShareInvitation> AddAsync(ShareInvitation invitation, CancellationToken ct = default)
    {
        _db.ShareInvitations.Add(invitation);
        await _db.SaveChangesAsync(ct);
        return invitation;
    }

    public async Task UpdateAsync(ShareInvitation invitation, CancellationToken ct = default)
    {
        _db.ShareInvitations.Update(invitation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ShareInvitation invitation, CancellationToken ct = default)
    {
        _db.ShareInvitations.Remove(invitation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ShareInvitation>> ListTouchingUserAsync(
        string userId, CancellationToken ct = default)
    {
        var items = await _db.ShareInvitations
            .Where(s => s.OwnerUserId == userId || s.GuestUserId == userId)
            .ToListAsync(ct);
        return items;
    }
}
