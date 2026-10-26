using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Portfolio.Api.Authentication;

// Separate scheme: this machine identity is never accepted by Admin JWT routes.
public sealed class DiscoveryImportAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DiscoveryImport";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configured = configuration["DiscoveryImport:Key"];
        var supplied = Request.Headers["X-Discovery-Key"];
        if (!Request.IsHttps || !bool.TryParse(configuration["DiscoveryImport:Enabled"], out var enabled) || !enabled ||
            configured is null || !System.Text.RegularExpressions.Regex.IsMatch(configured, "\\A[0-9a-fA-F]{64}\\z") ||
            supplied.Count != 1 || supplied[0] is not { Length: 64 } value ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(value)))
            return Task.FromResult(AuthenticateResult.Fail("Discovery import authentication required."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "discovery:import")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
