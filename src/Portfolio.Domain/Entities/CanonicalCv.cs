namespace Portfolio.Domain.Entities;

public sealed class CanonicalCv
{
    public Guid Id { get; set; }
    public short SingletonKey { get; set; } = 1;
    public string StorageKey { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string ContentHash { get; set; } = null!;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
