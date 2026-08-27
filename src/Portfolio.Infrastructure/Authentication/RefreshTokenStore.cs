using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.Authentication;

public sealed class RefreshTokenStore(ApplicationDbContext dbContext) : IRefreshTokenStore
{
    public async Task<RefreshRotationResult> RotateAsync(
        string currentTokenHash,
        Guid replacementTokenId,
        string replacementTokenHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var current = await dbContext.AdminRefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == currentTokenHash)
            .Select(token => new
            {
                token.Id,
                token.AdminUserId,
                token.ExpiresAt,
                token.RevokedAt,
                token.AdminUser.Email,
                token.AdminUser.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return new RefreshRotationResult(RefreshRotationStatus.Invalid);
        }

        if (current.RevokedAt is not null)
        {
            return new RefreshRotationResult(RefreshRotationStatus.Revoked);
        }

        if (current.ExpiresAt <= now)
        {
            return new RefreshRotationResult(RefreshRotationStatus.Expired);
        }

        if (!current.IsActive)
        {
            return new RefreshRotationResult(RefreshRotationStatus.AdminDisabled);
        }

        var replacement = new AdminRefreshToken
        {
            Id = replacementTokenId,
            AdminUserId = current.AdminUserId,
            TokenHash = replacementTokenHash,
            ExpiresAt = replacementExpiresAt,
            CreatedAt = now
        };
        dbContext.AdminRefreshTokens.Add(replacement);
        await dbContext.SaveChangesAsync(cancellationToken);

        var claimed = await ActiveClaimQuery(dbContext.AdminRefreshTokens, current.Id, now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.ReplacedByTokenId, replacementTokenId),
                cancellationToken);

        if (claimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.Entry(replacement).State = EntityState.Detached;
            return new RefreshRotationResult(RefreshRotationStatus.Revoked);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RefreshRotationResult(
            RefreshRotationStatus.Succeeded,
            current.AdminUserId,
            current.Email);
    }

    public Task RevokeAsync(
        string tokenHash,
        Guid adminUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        dbContext.AdminRefreshTokens
            .Where(token => token.TokenHash == tokenHash &&
                token.AdminUserId == adminUserId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

    internal static IQueryable<AdminRefreshToken> ActiveClaimQuery(
        DbSet<AdminRefreshToken> tokens,
        Guid tokenId,
        DateTimeOffset now) =>
        tokens.Where(token => token.Id == tokenId && token.RevokedAt == null && token.ExpiresAt > now);
}
