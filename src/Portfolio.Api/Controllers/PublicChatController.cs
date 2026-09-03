using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Api.Contracts.Common;
using Portfolio.Api.ChatProtection;
using Portfolio.Api.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Chat;

namespace Portfolio.Api.Controllers;

[ApiController,Route("api/v1/public/chat")]
public sealed class PublicChatController(IRequestDispatcher dispatcher,IClientIdentityProvider clientIdentity):ControllerBase
{
    [HttpPost("sessions")]public async Task<ActionResult<ApiResponse<ChatSessionResult>>> Create(CancellationToken ct){var result=await dispatcher.DispatchAsync(new CreateChatSessionCommand(),ct);return StatusCode(StatusCodes.Status201Created,ApiResponse<ChatSessionResult>.Ok(result));}
    [HttpPost("sessions/{sessionId:guid}/messages"),EnableRateLimiting(AuthenticationExtensions.PublicChatMessagePolicy)]public async Task<ActionResult<ApiResponse<ChatAnswerResult>>> Message(Guid sessionId,ChatMessageRequest request,CancellationToken ct)=>Ok(ApiResponse<ChatAnswerResult>.Ok(await dispatcher.DispatchAsync(new SendChatMessageCommand(sessionId,request.Message,clientIdentity.GetVisitorKey(HttpContext)),ct)));
    [HttpPost("messages/{messageId:guid}/feedback")]public async Task<ActionResult<ApiResponse<object>>> Feedback(Guid messageId,ChatFeedbackRequest request,CancellationToken ct){var id=await dispatcher.DispatchAsync(new SubmitChatFeedbackCommand(messageId,request.Rating,request.Comment),ct);return StatusCode(StatusCodes.Status201Created,ApiResponse<object>.Ok(new{id}));}
}
public sealed record ChatMessageRequest(string Message);public sealed record ChatFeedbackRequest(string Rating,string? Comment);
