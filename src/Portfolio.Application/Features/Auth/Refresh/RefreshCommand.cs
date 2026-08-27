using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Features.Auth.Refresh;

public sealed record RefreshCommand(string RefreshToken) : IRequest<RefreshResult>;
