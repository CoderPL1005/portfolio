using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/job-hunting/canonical-cv")]
public sealed class AdminCanonicalCvController(IRequestDispatcher dispatcher) : ControllerBase
{
    private const long RequestLimit = CanonicalCvUploadLimit + 65536;
    private const long CanonicalCvUploadLimit = 10L * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CanonicalCvResult>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse<CanonicalCvResult>.Ok(
            await dispatcher.DispatchAsync(new GetCanonicalCvQuery(), cancellationToken)));

    [HttpPut]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<ActionResult<ApiResponse<CanonicalCvResult>>> Put(
        IFormFile? file,
        [FromForm] int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            var missing = await dispatcher.DispatchAsync(
                new UploadCanonicalCvCommand(null, null, null, 0, expectedVersion), cancellationToken);
            return Ok(ApiResponse<CanonicalCvResult>.Ok(missing));
        }
        await using var content = file.OpenReadStream();
        var result = await dispatcher.DispatchAsync(new UploadCanonicalCvCommand(
            content, file.FileName, file.ContentType, file.Length, expectedVersion), cancellationToken);
        return Ok(ApiResponse<CanonicalCvResult>.Ok(result));
    }

    [HttpGet("content")]
    public async Task<IActionResult> Content(CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(new GetCanonicalCvContentQuery(), cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store, private";
        return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
    }
}
