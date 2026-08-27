using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class KnowledgeChunk
{
    public Guid Id { get; set; }
    public Guid KnowledgeDocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = null!;
    public int? TokenCount { get; set; }
    public string? ContentHash { get; set; }
    public float[] Embedding { get; set; } = null!;
    public string EmbeddingModel { get; set; } = null!;
    public JsonDocument Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public KnowledgeDocument KnowledgeDocument { get; set; } = null!;
}
