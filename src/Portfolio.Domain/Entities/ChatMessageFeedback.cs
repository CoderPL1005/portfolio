namespace Portfolio.Domain.Entities;

public sealed class ChatMessageFeedback
{
    public Guid Id { get; set; }
    public Guid ChatMessageId { get; set; }
    public string Rating { get; set; } = null!;
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;
}
