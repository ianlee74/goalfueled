using GoalFueled.Api.Auth;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoalFueled.Tests;

/// <summary>Shared fixtures: a fresh in-memory context per test, plus a stub identity provider.</summary>
internal static class TestHelpers
{
    public static GoalFueledDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<GoalFueledDbContext>()
            // Unique database name per call so tests are fully isolated.
            .UseInMemoryDatabase($"test-{Guid.NewGuid():N}")
            .Options;
        return new GoalFueledDbContext(options);
    }

    public static Authenticator NewAuthenticator(GoalFueledDbContext db, AuthenticatedIdentity identity)
    {
        var providers = new IIdentityProvider[] { new StubIdentityProvider(identity) };
        return new Authenticator(providers, new UserRepository(db), NullLogger<Authenticator>.Instance);
    }

    public static HttpRequest BlankRequest() => new DefaultHttpContext().Request;
}

/// <summary>An identity provider that always returns a preset identity (or none).</summary>
internal sealed class StubIdentityProvider : IIdentityProvider
{
    private readonly AuthenticatedIdentity? _identity;
    public StubIdentityProvider(AuthenticatedIdentity? identity) => _identity = identity;
    public string Key => _identity?.Provider ?? "stub";
    public Task<AuthenticatedIdentity?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
        => Task.FromResult(_identity);
}
