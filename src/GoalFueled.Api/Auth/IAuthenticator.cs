using GoalFueled.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace GoalFueled.Api.Auth;

/// <summary>
/// Turns an incoming request into the persisted <see cref="AppUser"/>, creating the profile on first
/// sight. Implements the rules from the spec: a profile is created and persisted after initial
/// authentication, and the very first user to join is granted the App Admin role.
/// </summary>
public interface IAuthenticator
{
    /// <summary>
    /// Resolves (and lazily provisions) the current user for the request, or null if the request
    /// carries no valid identity. The result is cached for the lifetime of the request scope.
    /// </summary>
    Task<AppUser?> GetCurrentUserAsync(HttpRequest request, CancellationToken ct = default);
}
