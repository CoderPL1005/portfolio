namespace Portfolio.Domain.Entities;

public sealed class JourneyItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public DateOnly? OccurredAt { get; set; }
    public string? IconKey { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
