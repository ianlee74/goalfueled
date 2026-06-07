using Microsoft.AspNetCore.Http;

namespace GoalFueled.Api.Auth;

/// <summary>
/// Resolves an <see cref="AuthenticatedIdentity"/> from an incoming request. This is the seam that
/// lets us run a local dev login today and swap in real social providers later without touching the
/// rest of the API. Multiple providers may be registered; the first to return a non-null identity wins.
/// </summary>
public interface IIdentityProvider
{
    /// <summary>Provider key (matches <see cref="AuthenticatedIdentity.Provider"/>).</summary>
    string Key { get; }

    /// <summary>Returns the identity if this provider can authenticate the request; otherwise null.</summary>
    Task<AuthenticatedIdentity?> ResolveAsync(HttpRequest request, CancellationToken ct = default);
}
