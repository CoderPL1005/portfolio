using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Common.Messaging;

public sealed class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Wrappers = new();

    public Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = (request.GetType(), typeof(TResponse));
        var wrapper = (IRequestHandlerWrapper<TResponse>)Wrappers.GetOrAdd(
            key,
            static types => Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(types.Request, types.Response))!);

        return wrapper.HandleAsync(request, serviceProvider, cancellationToken);
    }

    private interface IRequestHandlerWrapper<TResponse>
    {
        Task<TResponse> HandleAsync(
            IRequest<TResponse> request,
            IServiceProvider provider,
            CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapper<TRequest, TResponse> : IRequestHandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> HandleAsync(
            IRequest<TResponse> request,
            IServiceProvider provider,
            CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;
            var handler = provider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            var behaviors = provider
                .GetServices<IRequestBehavior<TRequest, TResponse>>()
                .ToArray();

            RequestHandlerDelegate<TResponse> pipeline =
                () => handler.HandleAsync(typedRequest, cancellationToken);

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                var behavior = behaviors[index];
                var next = pipeline;
                pipeline = () => behavior.HandleAsync(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
