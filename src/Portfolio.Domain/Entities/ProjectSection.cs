using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class ProjectSection
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string SectionType { get; set; } = null!;
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? ContentMarkdown { get; set; }
    public JsonDocument ContentJson { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Project Project { get; set; } = null!;
}
