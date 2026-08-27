using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Messaging;

namespace Portfolio.UnitTests.Foundation;

public sealed class RequestDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_resolves_handler_and_returns_result()
    {
        var handler = new TestHandler();
        var provider = new TestServiceProvider()
            .Add<IRequestHandler<TestRequest, string>>(handler)
            .Add<IEnumerable<IRequestBehavior<TestRequest, string>>>(
                Array.Empty<IRequestBehavior<TestRequest, string>>());
        var dispatcher = new RequestDispatcher(provider);

        var result = await dispatcher.DispatchAsync(new TestRequest("expected"));

        Assert.Equal("expected", result);
        Assert.True(handler.WasExecuted);
    }

    [Fact]
    public async Task DispatchAsync_passes_cancellation_token_to_handler()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new TestHandler();
        var provider = new TestServiceProvider()
            .Add<IRequestHandler<TestRequest, string>>(handler)
            .Add<IEnumerable<IRequestBehavior<TestRequest, string>>>(
                Array.Empty<IRequestBehavior<TestRequest, string>>());
        var dispatcher = new RequestDispatcher(provider);

        await dispatcher.DispatchAsync(new TestRequest("value"), cancellation.Token);

        Assert.Equal(cancellation.Token, handler.CancellationToken);
    }

    private sealed record TestRequest(string Value) : IRequest<string>;

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        public bool WasExecuted { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<string> HandleAsync(
            TestRequest request,
            CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            CancellationToken = cancellationToken;
            return Task.FromResult(request.Value);
        }
    }
}

internal sealed class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    public TestServiceProvider Add<TService>(TService service)
        where TService : notnull
    {
        _services[typeof(TService)] = service;
        return this;
    }

    public object? GetService(Type serviceType) =>
        _services.GetValueOrDefault(serviceType);
}
