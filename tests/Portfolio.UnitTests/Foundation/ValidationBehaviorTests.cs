using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Behaviors;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.UnitTests.Foundation;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_allows_valid_request_and_executes_next()
    {
        var nextExecuted = false;
        var behavior = new ValidationBehavior<TestRequest, string>(
            [new StubValidator([])]);

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
    }

    [Fact]
    public async Task HandleAsync_rejects_invalid_request_and_prevents_handler_execution()
    {
        var nextExecuted = false;
        var behavior = new ValidationBehavior<TestRequest, string>(
            [new StubValidator([new ValidationFailure("title", "Title is required.")])]);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.HandleAsync(
                new TestRequest(),
                () =>
                {
                    nextExecuted = true;
                    return Task.FromResult("handled");
                },
                CancellationToken.None));

        Assert.False(nextExecuted);
        Assert.Equal(["Title is required."], exception.Errors["title"]);
    }

    private sealed record TestRequest : IRequest<string>;

    private sealed class StubValidator(IReadOnlyCollection<ValidationFailure> failures)
        : IRequestValidator<TestRequest>
    {
        public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
            TestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(failures);
    }
}
