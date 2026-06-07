using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Api.Services;

/// <summary>
/// Admin-only user management: listing users and promoting/demoting the App Admin role. All
/// authorization is enforced here (acting-admin required) so the Function endpoints stay thin.
/// </summary>
public sealed class UserAdminService
{
    private readonly IUserRepository _users;

    public UserAdminService(IUserRepository users) => _users = users;

    /// <summary>Lists all users for the admin user-management screen. Acting-admin only.</summary>
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(AppUser actor, CancellationToken ct)
    {
        EnsureActingAdmin(actor);
        var users = await _users.ListAllAsync(ct);
        return users.Select(u => ToDto(u, actor)).ToList();
    }

    /// <summary>
    /// Promotes or demotes a user's App Admin role. Acting-admin only. Guards against locking the app
    /// out of all admins: the last remaining admin cannot be demoted (whether self or another).
    /// </summary>
    public async Task<AdminUserDto> SetAdminRoleAsync(
        AppUser actor, string userId, bool isAdmin, CancellationToken ct)
    {
        EnsureActingAdmin(actor);

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        // No-op requests short-circuit so we don't write or trip the last-admin guard needlessly.
        if (target.IsAdmin == isAdmin) return ToDto(target, actor);

        if (!isAdmin)
        {
            // Demotion: never allow the last remaining admin to lose the role (would lock everyone out).
            var adminCount = await _users.CountAdminsAsync(ct);
            if (adminCount <= 1)
                throw new ValidationException("Cannot demote the last remaining admin.");
            target.Roles.Remove(AppRole.AppAdmin);
        }
        else if (!target.Roles.Contains(AppRole.AppAdmin))
        {
            target.Roles.Add(AppRole.AppAdmin);
        }

        await _users.UpdateAsync(target, ct);
        return ToDto(target, actor);
    }

    private static void EnsureActingAdmin(AppUser user)
    {
        if (!user.IsActingAsAdmin)
            throw new ForbiddenException("This action requires admin access.");
    }

    private static AdminUserDto ToDto(AppUser u, AppUser actor) => new(
        u.Id, u.Email, u.DisplayName, u.AvatarUrl, u.Roles, u.IsAdmin, u.Id == actor.Id);
}
