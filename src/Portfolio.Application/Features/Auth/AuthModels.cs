namespace Portfolio.Application.Features.Auth;

public sealed record AdminSummary(Guid Id, string Email, string? FullName);

public sealed record LoginResult(
    string AccessToken,
    int ExpiresIn,
    AdminSummary Admin,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record RefreshResult(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentAdminResult(
    Guid Id,
    string Email,
    string? FullName,
    DateTimeOffset? LastLoginAt);
