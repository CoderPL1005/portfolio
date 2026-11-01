using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Authentication;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

// Private scheduled entrypoint. Same protected worker secret, authenticated run
// metadata and HTTPS gate as ingestion; no public or JWT-triggered scheduled runs.
[ApiController]
[Authorize(AuthenticationSchemes = DiscoveryExecutionAuthentication.SchemeName)]
[Route("api/v1/discovery-daily")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DiscoveryDailyController(IDiscoveryExecutionService service) : ControllerBase
{
    [HttpPost("start"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (User.FindFirst("discovery_attempt")?.Value != "1")
            throw new ConflictException("DISCOVERY_DAILY_RERUN_FORBIDDEN", "Scheduled executions cannot be rerun.");
        var expression = Request.Headers["X-Discovery-Daily-Slot"].ToString();
        return Accepted(ApiResponse<object>.Ok(await service.StartScheduledAsync(expression, ct)));
    }
}
