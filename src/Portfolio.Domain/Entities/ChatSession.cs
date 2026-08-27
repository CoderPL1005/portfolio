using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class ChatSession
{
    public Guid Id { get; set; }
    public Guid PublicSessionId { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int MessageCount { get; set; }
    public JsonDocument Metadata { get; set; } = null!;
}
