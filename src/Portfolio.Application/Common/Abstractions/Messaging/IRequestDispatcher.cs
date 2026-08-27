namespace Portfolio.Application.Common.Abstractions.Messaging;

public interface IRequestDispatcher
{
    Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
