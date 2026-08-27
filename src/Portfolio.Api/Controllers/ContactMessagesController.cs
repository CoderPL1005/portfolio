using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.ContactMessages;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Phase4C;

namespace Portfolio.Api.Controllers;

[ApiController, Route("api/v1/public/contact")]
public sealed class PublicContactController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting("public-contact")]
    public async Task<ActionResult<ApiResponse<ContactSubmissionResult>>> Submit(ContactRequest request, CancellationToken ct)
    { var result = await dispatcher.DispatchAsync(new SubmitContactMessageCommand(request.Name, request.Email, request.Subject, request.Message), ct); return StatusCode(StatusCodes.Status201Created, ApiResponse<ContactSubmissionResult>.Ok(result)); }
}
[ApiController, Authorize, Route("api/v1/admin/contact-messages")]
public sealed class AdminContactMessagesController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<PagedResult<ContactMessageResult>>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default) => Ok(ApiResponse<PagedResult<ContactMessageResult>>.Ok(await dispatcher.DispatchAsync(new GetContactMessagesQuery(page, pageSize, status), ct)));
    [HttpGet("{id:guid}")] public async Task<ActionResult<ApiResponse<ContactMessageResult>>> Get(Guid id, CancellationToken ct) => Ok(ApiResponse<ContactMessageResult>.Ok(await dispatcher.DispatchAsync(new GetContactMessageQuery(id), ct)));
    [HttpPatch("{id:guid}/status")] public async Task<ActionResult<ApiResponse<ContactMessageResult>>> Status(Guid id, ContactStatusRequest request, CancellationToken ct) => Ok(ApiResponse<ContactMessageResult>.Ok(await dispatcher.DispatchAsync(new UpdateContactMessageStatusCommand(id, request.Status), ct)));
}
public sealed record ContactRequest(string Name, string Email, string? Subject, string Message);
public sealed record ContactStatusRequest(string Status);
