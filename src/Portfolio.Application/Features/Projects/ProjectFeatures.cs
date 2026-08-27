using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Projects;

public sealed record GetProjectsQuery(int Page, int PageSize, string? Search, string? Status,
    bool? Featured, bool? IsPublished) : IRequest<PagedResult<ProjectListItemResult>>;
public sealed record GetProjectQuery(Guid Id) : IRequest<ProjectResult>;
public sealed record CreateProjectCommand(string Slug, string Title, string? Subtitle,
    string? ShortDescription, string? OverviewMarkdown, string? Role, int? TeamSize,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string? GithubUrl, string? LiveUrl,
    Guid? ThumbnailMediaId, bool Featured, bool IsPublished, int DisplayOrder,
    string? SeoTitle, string? SeoDescription, IReadOnlyCollection<Guid> TechnologyIds) : IRequest<ProjectResult>;
public sealed record UpdateProjectCommand(Guid Id, string Slug, string Title, string? Subtitle,
    string? ShortDescription, string? OverviewMarkdown, string? Role, int? TeamSize,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string? GithubUrl, string? LiveUrl,
    Guid? ThumbnailMediaId, bool Featured, bool IsPublished, int DisplayOrder,
    string? SeoTitle, string? SeoDescription, IReadOnlyCollection<Guid> TechnologyIds) : IRequest<ProjectResult>;
public sealed record DeleteProjectCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderProjectsCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetProjectsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetProjectsQuery, PagedResult<ProjectListItemResult>>
{
    public async Task<PagedResult<ProjectListItemResult>> HandleAsync(GetProjectsQuery r, CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(r.Search)) { var v = r.Search.Trim().ToLowerInvariant(); q = q.Where(x => x.Title.ToLower().Contains(v) || x.Slug.ToLower().Contains(v)); }
        if (!string.IsNullOrWhiteSpace(r.Status)) { var v = r.Status.Trim().ToUpperInvariant(); q = q.Where(x => x.Status == v); }
        if (r.Featured.HasValue) q = q.Where(x => x.Featured == r.Featured.Value);
        if (r.IsPublished.HasValue) q = q.Where(x => x.IsPublished == r.IsPublished.Value);
        var total = await q.CountAsync(ct); var items = await q.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize)
            .Select(x => new ProjectListItemResult(x.Id, x.Slug, x.Title, x.Role, x.Status, x.Featured, x.IsPublished, x.DisplayOrder, x.ThumbnailMedia == null ? null : x.ThumbnailMedia.PublicUrl, x.UpdatedAt)).ToListAsync(ct);
        return new(items, r.Page, r.PageSize, total, (int)Math.Ceiling(total / (double)r.PageSize));
    }
}
public sealed class GetProjectQueryHandler(IApplicationDbContext db) : IRequestHandler<GetProjectQuery, ProjectResult>
{ public Task<ProjectResult> HandleAsync(GetProjectQuery r, CancellationToken ct = default) => ProjectMutation.GetAsync(db, r.Id, ct); }

public sealed class CreateProjectCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<CreateProjectCommand, ProjectResult>
{
    public async Task<ProjectResult> HandleAsync(CreateProjectCommand r, CancellationToken ct = default)
    {
        await ProjectMutation.ValidateReferencesAsync(db, r.Slug, r.ThumbnailMediaId, r.TechnologyIds, null, ct); var now = clock.GetUtcNow();
        var x = new Project { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now }; ProjectMutation.Assign(x, r.Slug, r.Title, r.Subtitle, r.ShortDescription, r.OverviewMarkdown, r.Role, r.TeamSize, r.StartDate, r.EndDate, r.Status, r.GithubUrl, r.LiveUrl, r.ThumbnailMediaId, r.Featured, r.IsPublished, r.DisplayOrder, r.SeoTitle, r.SeoDescription);
        db.Projects.Add(x); db.ProjectTechnologies.AddRange(r.TechnologyIds.Select((id, order) => new ProjectTechnology { ProjectId = x.Id, TechnologyId = id, DisplayOrder = order })); await db.SaveChangesAsync(ct); return await ProjectMutation.GetAsync(db, x.Id, ct);
    }
}
public sealed class UpdateProjectCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateProjectCommand, ProjectResult>
{
    public async Task<ProjectResult> HandleAsync(UpdateProjectCommand r, CancellationToken ct = default)
    {
        var x = await db.Projects.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("PROJECT_NOT_FOUND", "The project was not found."); await ProjectMutation.ValidateReferencesAsync(db, r.Slug, r.ThumbnailMediaId, r.TechnologyIds, r.Id, ct);
        ProjectMutation.Assign(x, r.Slug, r.Title, r.Subtitle, r.ShortDescription, r.OverviewMarkdown, r.Role, r.TeamSize, r.StartDate, r.EndDate, r.Status, r.GithubUrl, r.LiveUrl, r.ThumbnailMediaId, r.Featured, r.IsPublished, r.DisplayOrder, r.SeoTitle, r.SeoDescription); x.UpdatedAt = clock.GetUtcNow();
        var old = await db.ProjectTechnologies.Where(y => y.ProjectId == x.Id).ToListAsync(ct); db.ProjectTechnologies.RemoveRange(old); db.ProjectTechnologies.AddRange(r.TechnologyIds.Select((id, order) => new ProjectTechnology { ProjectId = x.Id, TechnologyId = id, DisplayOrder = order })); await db.SaveChangesAsync(ct); return await ProjectMutation.GetAsync(db, x.Id, ct);
    }
}
public sealed class DeleteProjectCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteProjectCommand, bool>
{ public async Task<bool> HandleAsync(DeleteProjectCommand r, CancellationToken ct = default) { var x = await db.Projects.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("PROJECT_NOT_FOUND", "The project was not found."); db.Projects.Remove(x); await db.SaveChangesAsync(ct); return true; } }
public sealed class ReorderProjectsCommandHandler(IApplicationDbContext db) : IRequestHandler<ReorderProjectsCommand, bool>
{ public async Task<bool> HandleAsync(ReorderProjectsCommand r, CancellationToken ct = default) { var ids = r.Items.Select(x => x.Id).ToArray(); var rows = await db.Projects.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (rows.Count != ids.Length) throw new NotFoundException("PROJECT_NOT_FOUND", "One or more projects were not found."); var map = r.Items.ToDictionary(x => x.Id, x => x.DisplayOrder); foreach (var x in rows) x.DisplayOrder = map[x.Id]; await db.SaveChangesAsync(ct); return true; } }

public sealed class GetProjectsQueryValidator : IRequestValidator<GetProjectsQuery>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetProjectsQuery r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); if (r.Page < 1) f.Add(new("page", "Page must be at least 1.")); if (r.PageSize is < 1 or > 100) f.Add(new("pageSize", "Page size must be between 1 and 100.")); if (!string.IsNullOrWhiteSpace(r.Status) && !ProjectValidation.Statuses.Contains(r.Status.Trim().ToUpperInvariant())) f.Add(new("status", "Project status is invalid.")); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
public sealed class CreateProjectCommandValidator : IRequestValidator<CreateProjectCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateProjectCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(ProjectValidation.Validate(r.Slug, r.Title, r.Subtitle, r.Role, r.TeamSize, r.StartDate, r.EndDate, r.Status, r.GithubUrl, r.LiveUrl, r.DisplayOrder, r.SeoTitle, r.SeoDescription, r.TechnologyIds)); }
public sealed class UpdateProjectCommandValidator : IRequestValidator<UpdateProjectCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateProjectCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(ProjectValidation.Validate(r.Slug, r.Title, r.Subtitle, r.Role, r.TeamSize, r.StartDate, r.EndDate, r.Status, r.GithubUrl, r.LiveUrl, r.DisplayOrder, r.SeoTitle, r.SeoDescription, r.TechnologyIds)); }
public sealed class ReorderProjectsCommandValidator : IRequestValidator<ReorderProjectsCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderProjectsCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContentValidation.ReorderItems(f, r.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }

internal static class ProjectValidation
{
    public static readonly string[] Statuses = ["PLANNED", "IN_PROGRESS", "ACTIVE", "COMPLETED", "ARCHIVED"];
    public static List<ValidationFailure> Validate(string slug, string title, string? subtitle, string? role, int? teamSize, DateOnly? start, DateOnly? end, string status, string? github, string? live, int order, string? seoTitle, string? seoDescription, IReadOnlyCollection<Guid> technologyIds)
    { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "slug", slug, 180); if (!string.IsNullOrWhiteSpace(slug) && !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$")) f.Add(new("slug", "Slug must contain lowercase letters, numbers, and single hyphens only.")); ContentValidation.RequiredText(f, "title", title, 255); ContentValidation.OptionalText(f, "subtitle", subtitle, 500); ContentValidation.OptionalText(f, "role", role, 255); if (teamSize.HasValue && teamSize <= 0) f.Add(new("teamSize", "Team size must be greater than zero.")); ContentValidation.DateRange(f, "endDate", start, end); ContentValidation.RequiredText(f, "status", status, 50); if (!string.IsNullOrWhiteSpace(status) && !Statuses.Contains(status.Trim().ToUpperInvariant())) f.Add(new("status", "Project status is invalid.")); ContentValidation.HttpUrl(f, "githubUrl", github); ContentValidation.HttpUrl(f, "liveUrl", live); ContentValidation.DisplayOrder(f, order); ContentValidation.OptionalText(f, "seoTitle", seoTitle, 255); ContentValidation.OptionalText(f, "seoDescription", seoDescription, 500); if (technologyIds is null) f.Add(new("technologyIds", "Technology IDs are required.")); else if (technologyIds.Count != technologyIds.Distinct().Count()) f.Add(new("technologyIds", "Technology IDs must be unique.")); return f; }
}
internal static class ProjectMutation
{
    public static void Assign(Project x, string slug, string title, string? subtitle, string? shortDescription, string? overview, string? role, int? teamSize, DateOnly? start, DateOnly? end, string status, string? github, string? live, Guid? thumbnail, bool featured, bool published, int order, string? seoTitle, string? seoDescription)
    { x.Slug = slug.Trim(); x.Title = title.Trim(); x.Subtitle = subtitle?.Trim(); x.ShortDescription = shortDescription; x.OverviewMarkdown = overview; x.Role = role?.Trim(); x.TeamSize = teamSize; x.StartDate = start; x.EndDate = end; x.Status = status.Trim().ToUpperInvariant(); x.GithubUrl = github?.Trim(); x.LiveUrl = live?.Trim(); x.ThumbnailMediaId = thumbnail; x.Featured = featured; x.IsPublished = published; x.DisplayOrder = order; x.SeoTitle = seoTitle?.Trim(); x.SeoDescription = seoDescription?.Trim(); }
    public static async Task ValidateReferencesAsync(IApplicationDbContext db, string slug, Guid? thumbnail, IReadOnlyCollection<Guid> technologyIds, Guid? exceptId, CancellationToken ct)
    { var value = slug.Trim().ToLowerInvariant(); if (await db.Projects.AnyAsync(x => x.Id != exceptId && x.Slug.ToLower() == value, ct)) throw new ConflictException("PROJECT_SLUG_EXISTS", "A project with this slug already exists."); if (thumbnail.HasValue && !await db.MediaAssets.AnyAsync(x => x.Id == thumbnail.Value, ct)) throw new NotFoundException("MEDIA_NOT_FOUND", "The selected thumbnail media was not found."); var ids = technologyIds.Distinct().ToArray(); if (ids.Length != technologyIds.Count || await db.Technologies.CountAsync(x => ids.Contains(x.Id), ct) != ids.Length) throw new NotFoundException("TECHNOLOGY_NOT_FOUND", "One or more technologies were not found."); }
    public static async Task<ProjectResult> GetAsync(IApplicationDbContext db, Guid id, CancellationToken ct)
    {
        var x = await db.Projects.AsNoTracking().Include(x => x.ThumbnailMedia).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("PROJECT_NOT_FOUND", "The project was not found.");
        var tech = await db.ProjectTechnologies.AsNoTracking().Where(y => y.ProjectId == id).OrderBy(y => y.DisplayOrder).ThenBy(y => y.TechnologyId).Select(y => new ProjectTechnologyResult(y.TechnologyId, y.Technology.Name, y.Technology.Category, y.Technology.IconKey, y.DisplayOrder)).ToListAsync(ct);
        var sectionRows = await db.ProjectSections.AsNoTracking().Where(y => y.ProjectId == id).OrderBy(y => y.DisplayOrder).ThenBy(y => y.Id).ToListAsync(ct); var sections = sectionRows.Select(ProjectRelationMapping.Section).ToList();
        var media = await db.ProjectMedia.AsNoTracking().Where(y => y.ProjectId == id).OrderBy(y => y.DisplayOrder).ThenBy(y => y.Id).Select(ProjectRelationMapping.MediaProjection).ToListAsync(ct);
        return new(x.Id, x.Slug, x.Title, x.Subtitle, x.ShortDescription, x.OverviewMarkdown, x.Role, x.TeamSize, x.StartDate, x.EndDate, x.Status, x.GithubUrl, x.LiveUrl, x.ThumbnailMediaId, x.ThumbnailMedia?.PublicUrl, x.Featured, x.IsPublished, x.DisplayOrder, x.SeoTitle, x.SeoDescription, x.UpdatedAt, tech, sections, media);
    }
}
