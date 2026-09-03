namespace Portfolio.Domain.Entities;

public sealed class ChatUsageDaily
{
    public DateOnly UsageDate { get; set; }
    public string VisitorKey { get; set; } = null!;
    public int AcceptedMessageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
