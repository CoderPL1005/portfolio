using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Portfolio.Application.Features.JobHunting;
namespace Portfolio.Api.Authentication;

// Separate from Admin JWT and DiscoveryImport. The service independently verifies
// authenticated GitHub run metadata before any execution operation.
public sealed class DiscoveryExecutionAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,UrlEncoder encoder,IOptions<DiscoveryExecutionOptions> settings):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    public const string SchemeName="DiscoveryExecutionWorker";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var s=settings.Value;var key=Request.Headers["X-Discovery-Worker-Key"];var run=Request.Headers["X-GitHub-Run-Id"];var attempt=Request.Headers["X-GitHub-Run-Attempt"];
        if(!Request.IsHttps||!s.Enabled||!s.WorkerEnabled||!Regex.IsMatch(s.WorkerKey,"\\A[0-9a-fA-F]{64}\\z")||key.Count!=1||key[0] is not{Length:64} supplied||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s.WorkerKey),Encoding.UTF8.GetBytes(supplied)))return Task.FromResult(AuthenticateResult.Fail("Discovery worker authentication required."));
        if(run.Count!=1||!Regex.IsMatch(run[0]??"","^[1-9][0-9]{0,19}$")||attempt.Count!=1||!Regex.IsMatch(attempt[0]??"","^[1-9][0-9]{0,3}$"))return Task.FromResult(AuthenticateResult.Fail("Discovery worker execution binding required."));
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("scope","discovery:execution"),new("discovery_run",run[0]!),new("discovery_attempt",attempt[0]!)],SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,SchemeName)));
    }
}
