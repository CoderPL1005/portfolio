using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Experiences;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin/experiences")]
public sealed class AdminExperiencesController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AdminExperienceResult>>>> List([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(ApiResponse<IReadOnlyCollection<AdminExperienceResult>>.Ok(await dispatcher.DispatchAsync(new GetExperiencesQuery(search), cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminExperienceResult>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<AdminExperienceResult>.Ok(await dispatcher.DispatchAsync(new GetExperienceQuery(id), cancellationToken)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminExperienceResult>>> Create(ExperienceRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(request.ToCreateCommand(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, ApiResponse<AdminExperienceResult>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminExperienceResult>>> Update(Guid id, ExperienceRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<AdminExperienceResult>.Ok(await dispatcher.DispatchAsync(request.ToUpdateCommand(id), cancellationToken)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    { await dispatcher.DispatchAsync(new DeleteExperienceCommand(id), cancellationToken); return NoContent(); }

    [HttpPut("reorder")]
    public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken cancellationToken)
    { await dispatcher.DispatchAsync(new ReorderExperiencesCommand(request.Items), cancellationToken); return Ok(ApiResponse.Ok()); }
}

public sealed record ExperienceRequest(string CompanyName, string RoleTitle, string? Location,
    DateOnly StartDate, DateOnly? EndDate, bool IsCurrent, string? Summary,
    string? ResponsibilitiesMarkdown, string? CompanyUrl, int DisplayOrder,
    bool IsPublished, IReadOnlyCollection<Guid> TechnologyIds)
{
    public CreateExperienceCommand ToCreateCommand() => new(CompanyName, RoleTitle, Location, StartDate,
        EndDate, IsCurrent, Summary, ResponsibilitiesMarkdown, CompanyUrl, DisplayOrder,
        IsPublished, TechnologyIds);
    public UpdateExperienceCommand ToUpdateCommand(Guid id) => new(id, CompanyName, RoleTitle,
        Location, StartDate, EndDate, IsCurrent, Summary, ResponsibilitiesMarkdown, CompanyUrl,
        DisplayOrder, IsPublished, TechnologyIds);
}

public sealed record ReorderRequest(IReadOnlyCollection<ReorderItem> Items);
