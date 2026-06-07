using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

public class UserAdminServiceTests
{
    // Provisions an admin (first user) and a regular user against the same context, plus the service.
    private static async Task<(AppUser admin, AppUser user, UserRepository repo, UserAdminService svc)>
        SetupAsync(GoalFueledDbContext db)
    {
        var admin = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var repo = new UserRepository(db);
        return (admin!, user!, repo, new UserAdminService(repo));
    }

    [Fact]
    public async Task Acting_admin_lists_all_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        var users = await svc.ListAsync(admin, default);

        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.Id == admin.Id && u.IsAdmin && u.IsSelf);
        Assert.Contains(users, u => u.Id == user.Id && !u.IsAdmin && !u.IsSelf);
    }

    [Fact]
    public async Task Non_admin_cannot_list_users()
    {
        using var db = TestHelpers.NewContext();
        var (_, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListAsync(user, default));
    }

    [Fact]
    public async Task Admin_with_view_disabled_cannot_list_users()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        // Turning the admin view off means the user is no longer acting as an admin.
        admin.AdminViewEnabled = false;

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ListAsync(admin, default));
    }

    [Fact]
    public async Task Admin_can_promote_and_demote_another_user()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        var promoted = await svc.SetAdminRoleAsync(admin, user.Id, true, default);
        Assert.True(promoted.IsAdmin);
        Assert.Contains(AppRole.AppAdmin, (await repo.GetByIdAsync(user.Id))!.Roles);

        var demoted = await svc.SetAdminRoleAsync(admin, user.Id, false, default);
        Assert.False(demoted.IsAdmin);
        Assert.DoesNotContain(AppRole.AppAdmin, (await repo.GetByIdAsync(user.Id))!.Roles);
    }

    [Fact]
    public async Task Cannot_demote_the_last_remaining_admin()
    {
        using var db = TestHelpers.NewContext();
        var (admin, _, _, svc) = await SetupAsync(db);

        // The first user is the only admin; demoting them would lock everyone out.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetAdminRoleAsync(admin, admin.Id, false, default));
    }

    [Fact]
    public async Task Admin_can_demote_self_when_another_admin_exists()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, repo, svc) = await SetupAsync(db);

        // Promote the second user so two admins exist, then the first may step down.
        await svc.SetAdminRoleAsync(admin, user.Id, true, default);
        var demoted = await svc.SetAdminRoleAsync(admin, admin.Id, false, default);

        Assert.False(demoted.IsAdmin);
        Assert.Equal(1, await repo.CountAdminsAsync());
    }

    [Fact]
    public async Task Non_admin_cannot_change_roles()
    {
        using var db = TestHelpers.NewContext();
        var (admin, user, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetAdminRoleAsync(user, admin.Id, false, default));
    }
}
