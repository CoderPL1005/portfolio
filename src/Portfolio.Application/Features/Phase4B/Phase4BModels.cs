using System.Text.Json;

namespace Portfolio.Application.Features.Phase4B;

public sealed record TechnologyResult(Guid Id, string Name, string Category, string? IconKey,
    string? WebsiteUrl, int DisplayOrder, bool IsActive, DateTimeOffset UpdatedAt);
public sealed record SkillResult(Guid Id, string Name, string Category, string ExperienceLevel,
    string? Description, Guid? TechnologyId, int DisplayOrder, bool IsPublished,
    DateTimeOffset UpdatedAt);
public sealed record ProjectTechnologyResult(Guid TechnologyId, string Name, string Category,
    string? IconKey, int DisplayOrder);
public sealed record ProjectSectionResult(Guid Id, string SectionType, string? Title,
    string? Subtitle, string? ContentMarkdown, JsonElement Content, int DisplayOrder,
    bool IsVisible);
public sealed record ProjectMediaResult(Guid Id, Guid MediaAssetId, string MediaRole,
    string Url, string? AltText, string? Caption, int DisplayOrder);
public sealed record ProjectResult(Guid Id, string Slug, string Title, string? Subtitle,
    string? ShortDescription, string? OverviewMarkdown, string? Role, int? TeamSize,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string? GithubUrl,
    string? LiveUrl, Guid? ThumbnailMediaId, string? ThumbnailUrl, bool Featured,
    bool IsPublished, int DisplayOrder, string? SeoTitle, string? SeoDescription,
    DateTimeOffset UpdatedAt, IReadOnlyCollection<ProjectTechnologyResult> Technologies,
    IReadOnlyCollection<ProjectSectionResult> Sections, IReadOnlyCollection<ProjectMediaResult> Media);
public sealed record ProjectListItemResult(Guid Id, string Slug, string Title, string? Role,
    string Status, bool Featured, bool IsPublished, int DisplayOrder, string? ThumbnailUrl,
    DateTimeOffset UpdatedAt);
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize,
    int Total, int TotalPages);
public sealed record PublicProjectListItem(Guid Id, string Slug, string Title, string? Subtitle,
    string? ShortDescription, string? Role, string Status, bool Featured, string? ThumbnailUrl,
    IReadOnlyCollection<PublicTechnologyResult> Technologies);
public sealed record PublicTechnologyResult(Guid Id, string Name, string Category);
public sealed record PublicSkillResult(Guid Id, string Name, string Category, string ExperienceLevel,
    string? Description, Guid? TechnologyId);
public sealed record PublicProjectSectionResult(Guid Id, string SectionType, string? Title,
    string? Subtitle, string? ContentMarkdown, JsonElement Content, int DisplayOrder);
public sealed record PublicProjectMediaResult(Guid Id, string Role, string Url, string? AltText,
    string? Caption, int DisplayOrder);
public sealed record ProjectSeoResult(string? Title, string? Description);
public sealed record PublicProjectDetail(Guid Id, string Slug, string Title, string? Subtitle,
    string? ShortDescription, string? OverviewMarkdown, string? Role, int? TeamSize,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string? GithubUrl, string? LiveUrl,
    string? ThumbnailUrl, ProjectSeoResult Seo,
    IReadOnlyCollection<PublicTechnologyResult> Technologies,
    IReadOnlyCollection<PublicProjectSectionResult> Sections,
    IReadOnlyCollection<PublicProjectMediaResult> Media);
