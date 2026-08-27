using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Request {RequestName} started.", requestName);

        try
        {
            var response = await next();
            stopwatch.Stop();
            logger.LogInformation(
                "Request {RequestName} completed in {ElapsedMilliseconds} ms.",
                requestName,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "Request {RequestName} failed after {ElapsedMilliseconds} ms.",
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
