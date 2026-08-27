namespace Portfolio.Domain.Entities;

public sealed class SocialLink
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = null!;
    public string? Label { get; set; }
    public string Url { get; set; } = null!;
    public string? IconKey { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
