namespace Portfolio.Application.Common.Abstractions.Authentication;

public interface IRefreshTokenStore
{
    Task<RefreshRotationResult> RotateAsync(
        string currentTokenHash,
        Guid replacementTokenId,
        string replacementTokenHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        string tokenHash,
        Guid adminUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public enum RefreshRotationStatus
{
    Succeeded,
    Invalid,
    Expired,
    Revoked,
    AdminDisabled
}

public sealed record RefreshRotationResult(
    RefreshRotationStatus Status,
    Guid? AdminUserId = null,
    string? Email = null);
