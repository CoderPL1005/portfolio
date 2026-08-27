namespace Portfolio.Domain.Entities;

public sealed class Certificate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly? IssuedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public string? CredentialId { get; set; }
    public string? CredentialUrl { get; set; }
    public Guid? CertificateMediaId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public MediaAsset? CertificateMedia { get; set; }
}
