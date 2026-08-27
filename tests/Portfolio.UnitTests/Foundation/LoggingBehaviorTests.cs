using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Behaviors;

namespace Portfolio.UnitTests.Foundation;

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task HandleAsync_logs_and_executes_next()
    {
        var logger = new CollectingLogger<LoggingBehavior<TestRequest, string>>();
        var behavior = new LoggingBehavior<TestRequest, string>(logger);
        var nextExecuted = false;

        var result = await behavior.HandleAsync(
            new TestRequest(),
            () =>
            {
                nextExecuted = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        Assert.True(nextExecuted);
        Assert.Equal("handled", result);
        Assert.Contains(logger.Messages, message => message.Contains("started", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("completed", StringComparison.Ordinal));
    }

    private sealed record TestRequest : IRequest<string>;
}

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}
