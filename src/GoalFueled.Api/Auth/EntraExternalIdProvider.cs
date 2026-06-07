using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GoalFueled.Api.Auth;

/// <summary>
/// SEAM (not yet implemented): production identity provider backed by Microsoft Entra External ID,
/// which federates Google, Microsoft, and Facebook social logins. The frontend would acquire an
/// access token via MSAL and send it as a Bearer token; this provider validates the JWT and maps
/// its claims onto an <see cref="AuthenticatedIdentity"/>.
///
/// To finish this:
///   1. Provision an Entra External ID tenant + user flow; add Google/Microsoft/Facebook IdPs.
///   2. Register the SPA and API app registrations; expose an API scope.
///   3. Validate the Bearer token here against the tenant's OpenID metadata (issuer, audience,
///      signing keys) using Microsoft.IdentityModel.* and map sub/email/name claims.
///   4. Register this provider in Program.cs and set GoalFueled:Auth:EnableDevAuth = false.
///
/// Returns null today so it is inert even if accidentally registered.
/// </summary>
public sealed class EntraExternalIdProvider : IIdentityProvider
{
    private readonly ILogger<EntraExternalIdProvider> _logger;

    public EntraExternalIdProvider(ILogger<EntraExternalIdProvider> logger) => _logger = logger;

    public string Key => "entra";

    public Task<AuthenticatedIdentity?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning("EntraExternalIdProvider is not implemented; no identity resolved.");
        return Task.FromResult<AuthenticatedIdentity?>(null);
    }
}
