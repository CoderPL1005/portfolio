using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class JobApplicationDocument
{
    public Guid Id { get; set; }
    public Guid JobApplicationId { get; set; }
    public string DocumentType { get; set; } = null!;
    public string VersionLabel { get; set; } = null!;
    public string? FileName { get; set; }
    public string? StorageKey { get; set; }
    public string? ContentHash { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? PackageRevision { get; set; }
    public int? SourceCanonicalCvVersion { get; set; }
    public JsonDocument Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public JobApplication JobApplication { get; set; } = null!;
}
