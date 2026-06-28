using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GoalFueled.Api.Auth;

/// <summary>
/// Production identity provider backed by Microsoft Entra External ID, which federates Google (and
/// optionally Microsoft/Facebook) social logins. The SPA acquires an access token via MSAL and sends
/// it as a Bearer token; this provider validates the JWT against the tenant's published OpenID
/// metadata (issuer, audience, signing keys, lifetime) and maps its claims onto an
/// <see cref="AuthenticatedIdentity"/>.
///
/// Configuration (app settings; double-underscore in Azure):
///   GoalFueled:Auth:Entra:Authority        e.g. https://&lt;tenant&gt;.ciamlogin.com/&lt;tenant-guid&gt;/v2.0
///   GoalFueled:Auth:Entra:Audience         the API app registration's client id (or api://&lt;id&gt;)
///   GoalFueled:Auth:Entra:MetadataAddress  (optional) explicit OpenID metadata URL override
///
/// When Authority or Audience is missing the provider stays inert (returns null), so a partially
/// configured deployment fails closed rather than throwing.
/// </summary>
public sealed class EntraExternalIdProvider : IIdentityProvider
{
    public const string ProviderKey = "entra";

    private readonly ILogger<EntraExternalIdProvider> _logger;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private readonly string[] _validAudiences;
    private readonly bool _configured;

    public EntraExternalIdProvider(IConfiguration config, ILogger<EntraExternalIdProvider> logger)
    {
        _logger = logger;

        var authority = config["GoalFueled:Auth:Entra:Authority"]?.Trim();
        var audience = config["GoalFueled:Auth:Entra:Audience"]?.Trim();
        _configured = !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(audience);

        if (!_configured)
        {
            _validAudiences = Array.Empty<string>();
            _logger.LogInformation(
                "EntraExternalIdProvider is not configured (Authority/Audience unset); it will not resolve identities.");
            return;
        }

        // Accept both the bare client id and the api://<id> application id URI as audiences.
        _validAudiences = audience!.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
            ? new[] { audience, audience["api://".Length..] }
            : new[] { audience, $"api://{audience}" };

        var metadataAddress = config["GoalFueled:Auth:Entra:MetadataAddress"]?.Trim();
        if (string.IsNullOrWhiteSpace(metadataAddress))
        {
            metadataAddress = $"{authority!.TrimEnd('/')}/.well-known/openid-configuration";
        }

        // Fetches and caches signing keys, refreshing them automatically as the tenant rotates them.
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public string Key => ProviderKey;

    public async Task<AuthenticatedIdentity?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (!_configured || _configManager is null) return null;

        var token = ReadBearerToken(request);
        if (token is null) return null;

        try
        {
            var oidc = await _configManager.GetConfigurationAsync(ct);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = oidc.Issuer,
                ValidateAudience = true,
                ValidAudiences = _validAudiences,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = oidc.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            var result = await _handler.ValidateTokenAsync(token, parameters);
            if (!result.IsValid)
            {
                _logger.LogWarning(result.Exception, "Bearer token failed validation.");
                return null;
            }

            var claims = result.ClaimsIdentity;
            var subject = claims.FindFirst("sub")?.Value ?? claims.FindFirst("oid")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                _logger.LogWarning("Validated token has no subject (sub/oid) claim; rejecting.");
                return null;
            }

            var email = claims.FindFirst("email")?.Value
                ?? claims.FindFirst("emails")?.Value
                ?? claims.FindFirst("preferred_username")?.Value
                ?? $"{subject}@no-email.local";
            var name = claims.FindFirst("name")?.Value ?? email;
            var avatar = claims.FindFirst("picture")?.Value;

            return new AuthenticatedIdentity(ProviderKey, subject, email, name, avatar);
        }
        catch (Exception ex)
        {
            // Network/metadata failures must not 500 the request; fail closed as unauthenticated.
            _logger.LogWarning(ex, "Bearer token validation could not complete.");
            return null;
        }
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) return null;

        const string scheme = "Bearer ";
        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..].Trim()
            : null;
    }
}
