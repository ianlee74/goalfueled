using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Auth;

/// <summary>
/// Scoped per request. Tries each registered <see cref="IIdentityProvider"/> in order, then maps the
/// resolved identity to a persisted user, provisioning a new profile when needed.
/// </summary>
public sealed class Authenticator : IAuthenticator
{
    private readonly IEnumerable<IIdentityProvider> _providers;
    private readonly IUserRepository _users;
    private readonly ILogger<Authenticator> _logger;

    private bool _resolved;
    private AppUser? _cached;

    public Authenticator(IEnumerable<IIdentityProvider> providers, IUserRepository users, ILogger<Authenticator> logger)
    {
        _providers = providers;
        _users = users;
        _logger = logger;
    }

    public async Task<AppUser?> GetCurrentUserAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (_resolved) return _cached;
        _resolved = true;

        AuthenticatedIdentity? identity = null;
        foreach (var provider in _providers)
        {
            identity = await provider.ResolveAsync(request, ct);
            if (identity is not null) break;
        }

        if (identity is null) return _cached = null;

        var existing = await _users.GetByExternalLoginAsync(identity.Provider, identity.Subject, ct);
        if (existing is not null)
        {
            return _cached = existing;
        }

        // First user to join the app becomes the App Admin.
        var isFirstUser = !await _users.AnyAsync(ct);

        var user = new AppUser
        {
            Email = identity.Email,
            DisplayName = identity.DisplayName,
            AvatarUrl = identity.AvatarUrl,
            Roles = isFirstUser
                ? new List<AppRole> { AppRole.User, AppRole.AppAdmin }
                : new List<AppRole> { AppRole.User },
        };
        user.LinkLogin(identity.Provider, identity.Subject);

        await _users.AddAsync(user, ct);
        _logger.LogInformation("Provisioned new user {UserId} (admin={IsAdmin}) via {Provider}",
            user.Id, isFirstUser, identity.Provider);

        return _cached = user;
    }
}
