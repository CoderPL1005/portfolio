using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Education;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/education")]
public sealed class AdminEducationController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<EducationResult>>>> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<EducationResult>>.Ok(await dispatcher.DispatchAsync(new GetEducationsQuery(), ct)));
    [HttpGet("{id:guid}")] public async Task<ActionResult<ApiResponse<EducationResult>>> Get(Guid id, CancellationToken ct) => Ok(ApiResponse<EducationResult>.Ok(await dispatcher.DispatchAsync(new GetEducationQuery(id), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<EducationResult>>> Create(EducationRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return CreatedAtAction(nameof(Get), new { id = result.Id }, ApiResponse<EducationResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<EducationResult>>> Update(Guid id, EducationRequest request, CancellationToken ct) => Ok(ApiResponse<EducationResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteEducationCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderEducationsCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record EducationRequest(string Institution, string? Degree, string? FieldOfStudy, DateOnly? StartDate, DateOnly? EndDate, string? Description, string? Location, int DisplayOrder, bool IsPublished)
{
    public CreateEducationCommand Create() => new(Institution, Degree, FieldOfStudy, StartDate, EndDate, Description, Location, DisplayOrder, IsPublished);
    public UpdateEducationCommand Update(Guid id) => new(id, Institution, Degree, FieldOfStudy, StartDate, EndDate, Description, Location, DisplayOrder, IsPublished);
}
