namespace Portfolio.Domain.Entities;

public sealed class Experience
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = null!;
    public string RoleTitle { get; set; } = null!;
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Summary { get; set; }
    public string? ResponsibilitiesMarkdown { get; set; }
    public string? CompanyUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
