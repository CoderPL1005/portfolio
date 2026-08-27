using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await WriteErrorResponseAsync(context, exception);
        }
    }

    private async Task WriteErrorResponseAsync(HttpContext context, Exception exception)
    {
        var (statusCode, error) = exception switch
        {
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                new ApiError(
                    ValidationException.ErrorCode,
                    ValidationException.ErrorMessage,
                    validationException.Errors)),
            UnauthorizedException unauthorizedException => (
                StatusCodes.Status401Unauthorized,
                new ApiError(unauthorizedException.Code, unauthorizedException.Message)),
            ForbiddenException forbiddenException => (
                StatusCodes.Status403Forbidden,
                new ApiError(forbiddenException.Code, forbiddenException.Message)),
            NotFoundException notFoundException => (
                StatusCodes.Status404NotFound,
                new ApiError(notFoundException.Code, notFoundException.Message)),
            ConflictException conflictException => (
                StatusCodes.Status409Conflict,
                new ApiError(conflictException.Code, conflictException.Message)),
            _ => (
                StatusCodes.Status500InternalServerError,
                new ApiError("INTERNAL_ERROR", "An unexpected error occurred.")),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "An unexpected error occurred while processing {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "Request {Method} {Path} failed with {StatusCode} and error code {ErrorCode}.",
                context.Request.Method,
                context.Request.Path,
                statusCode,
                error.Code);
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(
            ApiResponse.Failure(error),
            cancellationToken: context.RequestAborted);
    }
}
