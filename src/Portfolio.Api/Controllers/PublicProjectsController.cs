using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Projects;

namespace Portfolio.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/v1/public/projects")]
public sealed class PublicProjectsController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicProjectListItem>>>> List([FromQuery] bool? featured, CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<PublicProjectListItem>>.Ok(await dispatcher.DispatchAsync(new GetPublicProjectsQuery(featured), ct)));
    [HttpGet("{slug}")] public async Task<ActionResult<ApiResponse<PublicProjectDetail>>> Get(string slug, CancellationToken ct) => Ok(ApiResponse<PublicProjectDetail>.Ok(await dispatcher.DispatchAsync(new GetPublicProjectBySlugQuery(slug), ct)));
}
