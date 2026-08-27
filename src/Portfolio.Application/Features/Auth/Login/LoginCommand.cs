using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;
