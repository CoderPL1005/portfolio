using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Authentication;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize(AuthenticationSchemes = DiscoveryImportAuthentication.SchemeName)]
[Route("api/v1/discovery-sync")]
public sealed class DiscoveryImportController(IRequestDispatcher dispatcher, IApplicationDbContext db) : ControllerBase
{
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        // Validate actual migrated persistence, not just the presence of a key.
        _ = await db.DiscoveryCandidates.AsNoTracking().AnyAsync(ct);
        _ = await db.DiscoveryCandidateReviews.AsNoTracking().AnyAsync(ct);
        _ = await db.DiscoveryCandidateReviewEvents.AsNoTracking().AnyAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { ready = true }));
    }

    [HttpPost("import"), RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<DiscoveryImportResult>>> Import([FromBody] DiscoveryReviewImport bundle, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryImportResult>.Ok(await dispatcher.DispatchAsync(new ImportDiscoveryReviewCommand(bundle), ct)));
}
