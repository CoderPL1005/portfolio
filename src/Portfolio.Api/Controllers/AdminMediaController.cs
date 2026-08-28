using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Models;
using Portfolio.Application.Features.Media;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/media")]
public sealed class AdminMediaController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<PagedResult<MediaAssetResult>>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? mediaType = null, [FromQuery] string? search = null, CancellationToken ct = default) => Ok(ApiResponse<PagedResult<MediaAssetResult>>.Ok(await dispatcher.DispatchAsync(new GetMediaQuery(page, pageSize, mediaType, search), ct)));
    [HttpPost, RequestSizeLimit(10 * 1024 * 1024 + 65536)] public async Task<ActionResult<ApiResponse<MediaAssetResult>>> Upload(IFormFile file, [FromForm] string mediaType, [FromForm] string? altText, CancellationToken ct) { await using var stream = file.OpenReadStream(); var result = await dispatcher.DispatchAsync(new UploadMediaCommand(stream, file.FileName, file.ContentType, file.Length, mediaType, altText), ct); return StatusCode(StatusCodes.Status201Created, ApiResponse<MediaAssetResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<MediaAssetResult>>> Update(Guid id, UpdateMediaRequest request, CancellationToken ct) => Ok(ApiResponse<MediaAssetResult>.Ok(await dispatcher.DispatchAsync(new UpdateMediaCommand(id, request.AltText, request.MediaType), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteMediaCommand(id), ct); return NoContent(); }
}
public sealed record UpdateMediaRequest(string? AltText, string MediaType);
