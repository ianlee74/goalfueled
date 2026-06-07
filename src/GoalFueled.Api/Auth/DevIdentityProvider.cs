using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Auth;

/// <summary>
/// Local development identity provider. The frontend "logs in" by picking a dev user; it then sends
/// that user's details on every request via headers. This lets the full app (including RBAC and the
/// first-user-becomes-admin rule) run end-to-end without real OAuth.
///
/// Headers:
///   X-Dev-Subject  (required) stable id for the dev user
///   X-Dev-Email    (optional)
///   X-Dev-Name     (optional)
///   X-Dev-Avatar   (optional)
///
/// Disabled unless <c>GoalFueled:Auth:EnableDevAuth</c> is true. It must NEVER be enabled in a real
/// deployment — these headers are unauthenticated and trivially spoofable.
/// </summary>
public sealed class DevIdentityProvider : IIdentityProvider
{
    public const string ProviderKey = "dev";

    private readonly bool _enabled;
    private readonly ILogger<DevIdentityProvider> _logger;

    public DevIdentityProvider(IConfiguration config, ILogger<DevIdentityProvider> logger)
    {
        _enabled = string.Equals(config["GoalFueled:Auth:EnableDevAuth"], "true", StringComparison.OrdinalIgnoreCase);
        _logger = logger;
    }

    public string Key => ProviderKey;

    public Task<AuthenticatedIdentity?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (!_enabled) return Task.FromResult<AuthenticatedIdentity?>(null);

        var subject = request.Headers["X-Dev-Subject"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject)) return Task.FromResult<AuthenticatedIdentity?>(null);

        var email = request.Headers["X-Dev-Email"].FirstOrDefault() ?? $"{subject}@dev.local";
        var name = request.Headers["X-Dev-Name"].FirstOrDefault() ?? subject;
        var avatar = request.Headers["X-Dev-Avatar"].FirstOrDefault();

        _logger.LogDebug("Dev auth resolved subject {Subject}", subject);
        return Task.FromResult<AuthenticatedIdentity?>(
            new AuthenticatedIdentity(ProviderKey, subject, email, name, avatar));
    }
}
