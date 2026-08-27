using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Skills;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/skills")]
public sealed class AdminSkillsController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SkillResult>>>> List([FromQuery] string? category, [FromQuery] string? experienceLevel, CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<SkillResult>>.Ok(await dispatcher.DispatchAsync(new GetSkillsQuery(category, experienceLevel), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<SkillResult>>> Create(SkillRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return StatusCode(StatusCodes.Status201Created, ApiResponse<SkillResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<SkillResult>>> Update(Guid id, SkillRequest request, CancellationToken ct) => Ok(ApiResponse<SkillResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteSkillCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderSkillsCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record SkillRequest(string Name, string Category, string ExperienceLevel, string? Description, Guid? TechnologyId, int DisplayOrder, bool IsPublished)
{ public CreateSkillCommand Create() => new(Name, Category, ExperienceLevel, Description, TechnologyId, DisplayOrder, IsPublished); public UpdateSkillCommand Update(Guid id) => new(id, Name, Category, ExperienceLevel, Description, TechnologyId, DisplayOrder, IsPublished); }
