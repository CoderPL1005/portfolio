using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class KnowledgeDocument
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = null!;
    public Guid? SourceRefId { get; set; }
    public string SourceKey { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public int Version { get; set; }
    public JsonDocument Metadata { get; set; } = null!;
    public bool IsActive { get; set; }
    public string IndexingStatus { get; set; } = null!;
    public DateTimeOffset? IndexedAt { get; set; }
    public string? LastIndexError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
