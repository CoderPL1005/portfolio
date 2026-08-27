using Portfolio.Application.Common.Abstractions.Messaging;

namespace Portfolio.Application.Features.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : IRequest<bool>;
