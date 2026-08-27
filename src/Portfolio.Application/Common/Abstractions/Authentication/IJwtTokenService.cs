namespace Portfolio.Application.Common.Abstractions.Authentication;

public interface IJwtTokenService
{
    AccessToken CreateAccessToken(Guid adminUserId, string email);
    RefreshToken CreateRefreshToken();
    string HashRefreshToken(string rawToken);
}

public sealed record AccessToken(string Value, int ExpiresInSeconds, DateTimeOffset ExpiresAt);
public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);
