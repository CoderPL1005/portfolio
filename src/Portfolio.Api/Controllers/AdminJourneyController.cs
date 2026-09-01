using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Journey;
using Portfolio.Application.Features.Phase4C;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/journey")]
public sealed class AdminJourneyController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet("timeline")] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AdminJourneyTimelineResult>>>> Timeline(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<AdminJourneyTimelineResult>>.Ok(await dispatcher.DispatchAsync(new GetAdminJourneyTimelineQuery(), ct)));
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<JourneyResult>>>> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<JourneyResult>>.Ok(await dispatcher.DispatchAsync(new GetJourneyItemsQuery(), ct)));
    [HttpGet("{id:guid}")] public async Task<ActionResult<ApiResponse<JourneyResult>>> Get(Guid id, CancellationToken ct) => Ok(ApiResponse<JourneyResult>.Ok(await dispatcher.DispatchAsync(new GetJourneyItemQuery(id), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<JourneyResult>>> Create(JourneyRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return CreatedAtAction(nameof(Get), new { id = result.Id }, ApiResponse<JourneyResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<JourneyResult>>> Update(Guid id, JourneyRequest request, CancellationToken ct) => Ok(ApiResponse<JourneyResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteJourneyItemCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderJourneyItemsCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record JourneyRequest(string Title, string? Subtitle, string? Description,
    DateOnly? OccurredAt, string? IconKey, int DisplayOrder, bool IsPublished)
{ public CreateJourneyItemCommand Create() => new(Title, Subtitle, Description, OccurredAt, IconKey, DisplayOrder, IsPublished); public UpdateJourneyItemCommand Update(Guid id) => new(id, Title, Subtitle, Description, OccurredAt, IconKey, DisplayOrder, IsPublished); }
