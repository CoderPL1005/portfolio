using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Behaviors;
using Portfolio.Application.Common.Messaging;

namespace Portfolio.UnitTests.Foundation;

public sealed class PipelineOrderTests
{
    [Fact]
    public async Task Dispatcher_executes_validation_then_logging_then_handler()
    {
        var events = new List<string>();
        var validation = new ValidationBehavior<TestRequest, string>(
            [new TrackingValidator(events)]);
        var logging = new LoggingBehavior<TestRequest, string>(
            new TrackingLogger<LoggingBehavior<TestRequest, string>>(events));
        var provider = new TestServiceProvider()
            .Add<IRequestHandler<TestRequest, string>>(new TrackingHandler(events))
            .Add<IEnumerable<IRequestBehavior<TestRequest, string>>>([validation, logging]);
        var dispatcher = new RequestDispatcher(provider);

        await dispatcher.DispatchAsync(new TestRequest());

        Assert.Equal(["validation", "logging", "handler"], events.Take(3));
    }

    private sealed record TestRequest : IRequest<string>;

    private sealed class TrackingValidator(List<string> events) : IRequestValidator<TestRequest>
    {
        public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
            TestRequest request,
            CancellationToken cancellationToken = default)
        {
            events.Add("validation");
            return Task.FromResult<IReadOnlyCollection<ValidationFailure>>([]);
        }
    }

    private sealed class TrackingHandler(List<string> events) : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(
            TestRequest request,
            CancellationToken cancellationToken = default)
        {
            events.Add("handler");
            return Task.FromResult("handled");
        }
    }

    private sealed class TrackingLogger<T>(List<string> events) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains("started", StringComparison.Ordinal))
            {
                events.Add("logging");
            }
        }
    }
}
