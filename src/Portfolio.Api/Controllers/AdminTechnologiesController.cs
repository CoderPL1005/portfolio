using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Technologies;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/technologies")]
public sealed class AdminTechnologiesController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<TechnologyResult>>>> List([FromQuery] string? search, [FromQuery] string? category, CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<TechnologyResult>>.Ok(await dispatcher.DispatchAsync(new GetTechnologiesQuery(search, category), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<TechnologyResult>>> Create(TechnologyRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return StatusCode(StatusCodes.Status201Created, ApiResponse<TechnologyResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<TechnologyResult>>> Update(Guid id, TechnologyRequest request, CancellationToken ct) => Ok(ApiResponse<TechnologyResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteTechnologyCommand(id), ct); return NoContent(); }
}
public sealed record TechnologyRequest(string Name, string Category, string? IconKey, string? WebsiteUrl, int DisplayOrder, bool IsActive)
{ public CreateTechnologyCommand Create() => new(Name, Category, IconKey, WebsiteUrl, DisplayOrder, IsActive); public UpdateTechnologyCommand Update(Guid id) => new(id, Name, Category, IconKey, WebsiteUrl, DisplayOrder, IsActive); }
