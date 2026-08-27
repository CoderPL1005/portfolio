using Portfolio.Api.Contracts.Common;

namespace Portfolio.Api.Middleware;

public sealed class AuthenticationResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.HasStarted || context.Response.ContentLength is > 0)
        {
            return;
        }

        ApiError? error = context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => new("UNAUTHORIZED", "Authentication is required or invalid."),
            StatusCodes.Status403Forbidden => new("FORBIDDEN", "The operation is forbidden."),
            _ => null
        };

        if (error is not null)
        {
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Failure(error),
                cancellationToken: context.RequestAborted);
        }
    }
}
