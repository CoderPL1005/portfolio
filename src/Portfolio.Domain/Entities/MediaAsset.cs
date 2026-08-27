namespace Portfolio.Domain.Entities;

public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public string StorageKey { get; set; } = null!;
    public string PublicUrl { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string? MimeType { get; set; }
    public long? FileSize { get; set; }
    public string? AltText { get; set; }
    public string MediaType { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
