namespace Portfolio.Domain.Entities;

public sealed class ChatMessageSource
{
    public Guid Id { get; set; }
    public Guid ChatMessageId { get; set; }
    public Guid KnowledgeChunkId { get; set; }
    public int Rank { get; set; }
    public decimal? SimilarityScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;
    public KnowledgeChunk KnowledgeChunk { get; set; } = null!;
}
