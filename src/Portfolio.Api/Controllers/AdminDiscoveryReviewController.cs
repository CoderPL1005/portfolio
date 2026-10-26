using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/job-hunting/discovery")]
public sealed class AdminDiscoveryReviewController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpPost("demo-reset-plan"), RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<ApiResponse<DiscoveryDemoResetPlan>>> ResetPlan([FromBody] PlanDiscoveryDemoResetQuery query, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryDemoResetPlan>.Ok(await dispatcher.DispatchAsync(query, ct)));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<DiscoveryReviewList>>> List([FromQuery] GetDiscoveryReviewQuery query, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryReviewList>.Ok(await dispatcher.DispatchAsync(query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DiscoveryReviewDetail>>> Detail(Guid id, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryReviewDetail>.Ok(await dispatcher.DispatchAsync(new GetDiscoveryCandidateQuery(id), ct)));
    [HttpPost("{id:guid}/reviews"), RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<ApiResponse<HumanReviewSummary>>> Review(Guid id, [FromBody] DiscoveryReviewWrite review, CancellationToken ct) =>
        Ok(ApiResponse<HumanReviewSummary>.Ok(await dispatcher.DispatchAsync(new SaveDiscoveryHumanReviewCommand(id, review), ct)));
    [HttpGet("{id:guid}/reviews")]
    public async Task<ActionResult<ApiResponse<DiscoveryReviewHistory>>> History(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse<DiscoveryReviewHistory>.Ok(await dispatcher.DispatchAsync(new GetDiscoveryReviewHistoryQuery(id, page, pageSize), ct)));
    [HttpGet("quality-metrics")]
    public async Task<ActionResult<ApiResponse<DiscoveryQualityReport>>> Metrics([FromQuery] GetDiscoveryQualityMetricsQuery query, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryQualityReport>.Ok(await dispatcher.DispatchAsync(query, ct)));
    [HttpPost("import"), RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<DiscoveryImportResult>>> Import([FromBody] DiscoveryReviewImport bundle, CancellationToken ct) =>
        Ok(ApiResponse<DiscoveryImportResult>.Ok(await dispatcher.DispatchAsync(new ImportDiscoveryReviewCommand(bundle), ct)));
}
