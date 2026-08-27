using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Auth.Login;

public sealed class LoginCommandHandler(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> HandleAsync(
        LoginCommand request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var admin = await dbContext.AdminUsers.SingleOrDefaultAsync(
            item => item.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (admin is null)
        {
            _ = passwordHasher.Hash(request.Password);
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        if (!passwordHasher.Verify(admin.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        if (!admin.IsActive)
        {
            throw new ForbiddenException("ADMIN_DISABLED", "The admin account is disabled.");
        }

        var now = timeProvider.GetUtcNow();
        var accessToken = jwtTokenService.CreateAccessToken(admin.Id, admin.Email);
        var refreshToken = jwtTokenService.CreateRefreshToken();
        dbContext.AdminRefreshTokens.Add(new AdminRefreshToken
        {
            Id = Guid.NewGuid(),
            AdminUserId = admin.Id,
            TokenHash = jwtTokenService.HashRefreshToken(refreshToken.Value),
            ExpiresAt = refreshToken.ExpiresAt,
            CreatedAt = now
        });
        admin.LastLoginAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            accessToken.Value,
            accessToken.ExpiresInSeconds,
            new AdminSummary(admin.Id, admin.Email, admin.FullName),
            refreshToken.Value,
            refreshToken.ExpiresAt);
    }
}
