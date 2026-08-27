using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.Auth.Refresh;

public sealed class RefreshCommandHandler(
    IRefreshTokenStore refreshTokenStore,
    IJwtTokenService jwtTokenService,
    TimeProvider timeProvider) : IRequestHandler<RefreshCommand, RefreshResult>
{
    public async Task<RefreshResult> HandleAsync(
        RefreshCommand request,
        CancellationToken cancellationToken = default)
    {
        var replacement = jwtTokenService.CreateRefreshToken();
        var replacementId = Guid.NewGuid();
        var rotation = await refreshTokenStore.RotateAsync(
            jwtTokenService.HashRefreshToken(request.RefreshToken),
            replacementId,
            jwtTokenService.HashRefreshToken(replacement.Value),
            replacement.ExpiresAt,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (rotation.Status == RefreshRotationStatus.AdminDisabled)
        {
            throw new ForbiddenException("ADMIN_DISABLED", "The admin account is disabled.");
        }

        if (rotation.Status != RefreshRotationStatus.Succeeded ||
            rotation.AdminUserId is null || rotation.Email is null)
        {
            throw CreateTokenException(rotation.Status);
        }

        var accessToken = jwtTokenService.CreateAccessToken(rotation.AdminUserId.Value, rotation.Email);
        return new RefreshResult(
            accessToken.Value,
            accessToken.ExpiresInSeconds,
            replacement.Value,
            replacement.ExpiresAt);
    }

    private static UnauthorizedException CreateTokenException(RefreshRotationStatus status) => status switch
    {
        RefreshRotationStatus.Expired => new("REFRESH_TOKEN_EXPIRED", "The refresh token has expired."),
        RefreshRotationStatus.Revoked => new("REFRESH_TOKEN_REVOKED", "The refresh token has been revoked."),
        _ => new("INVALID_REFRESH_TOKEN", "The refresh token is invalid.")
    };
}
