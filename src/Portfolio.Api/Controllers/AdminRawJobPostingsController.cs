using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/raw-job-postings")]
public sealed class AdminRawJobPostingsController(IRequestDispatcher dispatcher) : ControllerBase
{
    private const long RequestLimit = 52L * 1024 * 1024;

    [HttpPost("screenshots")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<ActionResult<ApiResponse<ScreenshotSubmissionResult>>> SubmitScreenshots(
        [FromForm] Guid submissionId,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var uploads = new List<ScreenshotUpload>(files.Count);
        var streams = new List<Stream>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                uploads.Add(new(stream, file.ContentType, file.Length));
            }
            var result = await dispatcher.DispatchAsync(
                new SubmitJobScreenshotsCommand(submissionId, uploads), cancellationToken);
            return result.Created
                ? StatusCode(StatusCodes.Status201Created, ApiResponse<ScreenshotSubmissionResult>.Ok(result))
                : Ok(ApiResponse<ScreenshotSubmissionResult>.Ok(result));
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }
    }
}
