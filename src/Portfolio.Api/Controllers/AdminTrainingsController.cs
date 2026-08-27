using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.Trainings;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/trainings")]
public sealed class AdminTrainingsController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<TrainingResult>>>> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<TrainingResult>>.Ok(await dispatcher.DispatchAsync(new GetTrainingsQuery(), ct)));
    [HttpGet("{id:guid}")] public async Task<ActionResult<ApiResponse<TrainingResult>>> Get(Guid id, CancellationToken ct) => Ok(ApiResponse<TrainingResult>.Ok(await dispatcher.DispatchAsync(new GetTrainingQuery(id), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<TrainingResult>>> Create(TrainingRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return CreatedAtAction(nameof(Get), new { id = result.Id }, ApiResponse<TrainingResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<TrainingResult>>> Update(Guid id, TrainingRequest request, CancellationToken ct) => Ok(ApiResponse<TrainingResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteTrainingCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderTrainingsCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record TrainingRequest(string Title, string? Provider, string? Description, DateOnly? StartDate, DateOnly? EndDate, string? CredentialUrl, int DisplayOrder, bool IsPublished)
{
    public CreateTrainingCommand Create() => new(Title, Provider, Description, StartDate, EndDate, CredentialUrl, DisplayOrder, IsPublished);
    public UpdateTrainingCommand Update(Guid id) => new(id, Title, Provider, Description, StartDate, EndDate, CredentialUrl, DisplayOrder, IsPublished);
}
