namespace Portfolio.Application.Common.Abstractions.Messaging;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
