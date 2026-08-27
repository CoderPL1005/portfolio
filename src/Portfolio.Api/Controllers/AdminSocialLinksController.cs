using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.SocialLinks;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/social-links")]
public sealed class AdminSocialLinksController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SocialLinkResult>>>> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<SocialLinkResult>>.Ok(await dispatcher.DispatchAsync(new GetSocialLinksQuery(), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<SocialLinkResult>>> Create(SocialLinkRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return StatusCode(StatusCodes.Status201Created, ApiResponse<SocialLinkResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<SocialLinkResult>>> Update(Guid id, SocialLinkRequest request, CancellationToken ct) => Ok(ApiResponse<SocialLinkResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteSocialLinkCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderSocialLinksCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record SocialLinkRequest(string Platform, string? Label, string Url, string? IconKey,
    int DisplayOrder, bool IsVisible)
{ public CreateSocialLinkCommand Create() => new(Platform, Label, Url, IconKey, DisplayOrder, IsVisible); public UpdateSocialLinkCommand Update(Guid id) => new(id, Platform, Label, Url, IconKey, DisplayOrder, IsVisible); }
