namespace Portfolio.Domain.Entities;

public sealed class AdminRefreshToken
{
    public Guid Id { get; set; }
    public Guid AdminUserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AdminUser AdminUser { get; set; } = null!;
    public AdminRefreshToken? ReplacedByToken { get; set; }
}
