namespace Portfolio.Domain.Entities;

public sealed class Training
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? CredentialUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
