using GoalFueled.Api.Auth;
using GoalFueled.Domain;

namespace GoalFueled.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task First_user_is_provisioned_as_app_admin()
    {
        using var db = TestHelpers.NewContext();
        var auth = TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "subject-1", "first@dev.local", "First User"));

        var user = await auth.GetCurrentUserAsync(TestHelpers.BlankRequest());

        Assert.NotNull(user);
        Assert.True(user!.IsAdmin);
        Assert.Contains(AppRole.AppAdmin, user.Roles);
    }

    [Fact]
    public async Task Second_user_is_a_regular_user()
    {
        using var db = TestHelpers.NewContext();

        // First user (becomes admin).
        await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "subject-1", "first@dev.local", "First"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        // Second, distinct user.
        var second = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "subject-2", "second@dev.local", "Second"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());

        Assert.NotNull(second);
        Assert.False(second!.IsAdmin);
        Assert.Equal(new[] { AppRole.User }, second.Roles);
    }

    [Fact]
    public async Task Same_identity_resolves_to_the_same_profile()
    {
        using var db = TestHelpers.NewContext();
        var identity = new AuthenticatedIdentity("dev", "subject-1", "first@dev.local", "First");

        var a = await TestHelpers.NewAuthenticator(db, identity).GetCurrentUserAsync(TestHelpers.BlankRequest());
        var b = await TestHelpers.NewAuthenticator(db, identity).GetCurrentUserAsync(TestHelpers.BlankRequest());

        Assert.Equal(a!.Id, b!.Id);
    }

    [Fact]
    public async Task No_identity_yields_no_user()
    {
        using var db = TestHelpers.NewContext();
        var auth = TestHelpers.NewAuthenticator(db, null!); // stub returns no identity

        var user = await auth.GetCurrentUserAsync(TestHelpers.BlankRequest());

        Assert.Null(user);
    }
}
