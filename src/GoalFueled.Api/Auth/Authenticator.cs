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
    /// <summary>
    /// Header carrying the id of an account the signed-in user wants to operate as. Honored only when the
    /// caller holds an active share grant over that account; otherwise ignored (the caller stays themselves).
    /// </summary>
    public const string ActAsHeader = "X-Act-As-User";

    private readonly IEnumerable<IIdentityProvider> _providers;
    private readonly IUserRepository _users;
    private readonly IShareInvitationRepository _shares;
    private readonly ILogger<Authenticator> _logger;

    private bool _resolved;
    private AppUser? _cached;

    public Authenticator(
        IEnumerable<IIdentityProvider> providers,
        IUserRepository users,
        IShareInvitationRepository shares,
        ILogger<Authenticator> logger)
    {
        _providers = providers;
        _users = users;
        _shares = shares;
        _logger = logger;
    }

    public async Task<AppUser?> GetCurrentUserAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (_resolved) return _cached;
        _resolved = true;

        var signedIn = await ResolveSignedInUserAsync(request, ct);
        if (signedIn is null) return _cached = null;

        // Account switching: if the caller asked to act as another account and holds an active share grant
        // over it, return that owner with the grant's permissions stamped on as transient acting-as state.
        var actAsId = request.Headers[ActAsHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(actAsId) && actAsId != signedIn.Id)
        {
            var grant = await _shares.GetActiveGrantAsync(actAsId, signedIn.Id, ct);
            if (grant is not null)
            {
                var owner = await _users.GetByIdAsync(actAsId, ct);
                if (owner is not null)
                {
                    owner.ActingViaShareGuestUserId = signedIn.Id;
                    owner.ActingSharePermissions = grant.Permissions;
                    _logger.LogInformation("User {GuestId} acting as {OwnerId} via share {ShareId}",
                        signedIn.Id, owner.Id, grant.Id);
                    return _cached = owner;
                }
            }
            // No valid grant: silently fall back to the signed-in user (never escalate).
            _logger.LogWarning("User {GuestId} requested act-as {OwnerId} without an active grant",
                signedIn.Id, actAsId);
        }

        return _cached = signedIn;
    }

    /// <summary>Resolves the real signed-in user from the request, provisioning a profile on first sight.</summary>
    private async Task<AppUser?> ResolveSignedInUserAsync(HttpRequest request, CancellationToken ct)
    {
        AuthenticatedIdentity? identity = null;
        foreach (var provider in _providers)
        {
            identity = await provider.ResolveAsync(request, ct);
            if (identity is not null) break;
        }

        if (identity is null) return null;

        var existing = await _users.GetByExternalLoginAsync(identity.Provider, identity.Subject, ct);
        if (existing is not null) return existing;

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

        return user;
    }
}
