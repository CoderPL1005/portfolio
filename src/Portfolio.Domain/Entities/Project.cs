namespace Portfolio.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string? ShortDescription { get; set; }
    public string? OverviewMarkdown { get; set; }
    public string? Role { get; set; }
    public int? TeamSize { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = null!;
    public string? GithubUrl { get; set; }
    public string? LiveUrl { get; set; }
    public Guid? ThumbnailMediaId { get; set; }
    public bool Featured { get; set; }
    public bool IsPublished { get; set; }
    public int DisplayOrder { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public MediaAsset? ThumbnailMedia { get; set; }
}
