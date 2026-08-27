namespace Portfolio.Domain.Entities;

public sealed class ContactMessage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Subject { get; set; }
    public string Message { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}
