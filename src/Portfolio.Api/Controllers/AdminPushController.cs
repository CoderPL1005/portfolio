using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PushNotifications;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/push")]
public sealed class AdminPushController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpPost("subscriptions")]
    public async Task<ActionResult<ApiResponse>> Register(
        PushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        await dispatcher.DispatchAsync(
            new RegisterPushSubscriptionCommand(request.Endpoint, request.P256dh, request.Auth),
            cancellationToken);
        return Ok(ApiResponse.Ok());
    }

    [HttpDelete("subscriptions")]
    public async Task<ActionResult<ApiResponse>> Disable(
        DisablePushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        await dispatcher.DispatchAsync(new DisablePushSubscriptionCommand(request.Endpoint), cancellationToken);
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("test")]
    public async Task<ActionResult<ApiResponse<PushTestSummary>>> Test(CancellationToken cancellationToken) =>
        Ok(ApiResponse<PushTestSummary>.Ok(
            await dispatcher.DispatchAsync(new SendTestPushNotificationCommand(), cancellationToken)));
}

public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
public sealed record DisablePushSubscriptionRequest(string Endpoint);
