using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.Auth.Logout;

public sealed class LogoutCommandHandler(
    IRefreshTokenStore refreshTokenStore,
    IJwtTokenService jwtTokenService,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<LogoutCommand, bool>
{
    public async Task<bool> HandleAsync(
        LogoutCommand request,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.AdminUserId is null)
        {
            throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        }

        await refreshTokenStore.RevokeAsync(
            jwtTokenService.HashRefreshToken(request.RefreshToken),
            currentUser.AdminUserId.Value,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return true;
    }
}
