using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;

namespace Portfolio.Application.Features.Projects;

public sealed record GetPublicProjectsQuery(bool? Featured) : IRequest<IReadOnlyCollection<PublicProjectListItem>>;
public sealed record GetPublicProjectBySlugQuery(string Slug) : IRequest<PublicProjectDetail>;

public sealed class GetPublicProjectsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetPublicProjectsQuery, IReadOnlyCollection<PublicProjectListItem>>
{
    public async Task<IReadOnlyCollection<PublicProjectListItem>> HandleAsync(GetPublicProjectsQuery r, CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking().Where(x => x.IsPublished); if (r.Featured.HasValue) q = q.Where(x => x.Featured == r.Featured.Value);
        var projects = await q.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => new { x.Id, x.Slug, x.Title, x.Subtitle, x.ShortDescription, x.Role, x.Status, x.Featured, ThumbnailUrl = x.ThumbnailMedia == null ? null : x.ThumbnailMedia.PublicUrl }).ToListAsync(ct);
        var ids = projects.Select(x => x.Id).ToArray(); var links = await db.ProjectTechnologies.AsNoTracking().Where(x => ids.Contains(x.ProjectId) && x.Technology.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.TechnologyId).Select(x => new { x.ProjectId, Item = new PublicTechnologyResult(x.TechnologyId, x.Technology.Name, x.Technology.Category) }).ToListAsync(ct);
        return projects.Select(x => new PublicProjectListItem(x.Id, x.Slug, x.Title, x.Subtitle, x.ShortDescription, x.Role, x.Status, x.Featured, x.ThumbnailUrl, links.Where(y => y.ProjectId == x.Id).Select(y => y.Item).ToList())).ToList();
    }
}
public sealed class GetPublicProjectBySlugQueryHandler(IApplicationDbContext db) : IRequestHandler<GetPublicProjectBySlugQuery, PublicProjectDetail>
{
    public async Task<PublicProjectDetail> HandleAsync(GetPublicProjectBySlugQuery r, CancellationToken ct = default)
    {
        var slug = r.Slug.Trim().ToLowerInvariant(); var x = await db.Projects.AsNoTracking().Include(x => x.ThumbnailMedia).SingleOrDefaultAsync(x => x.IsPublished && x.Slug.ToLower() == slug, ct) ?? throw new NotFoundException("PROJECT_NOT_FOUND", "The project was not found.");
        var tech = await db.ProjectTechnologies.AsNoTracking().Where(y => y.ProjectId == x.Id && y.Technology.IsActive).OrderBy(y => y.DisplayOrder).ThenBy(y => y.TechnologyId).Select(y => new PublicTechnologyResult(y.TechnologyId, y.Technology.Name, y.Technology.Category)).ToListAsync(ct);
        var sectionRows = await db.ProjectSections.AsNoTracking().Where(y => y.ProjectId == x.Id && y.IsVisible).OrderBy(y => y.DisplayOrder).ThenBy(y => y.Id).ToListAsync(ct); var sections = sectionRows.Select(y => new PublicProjectSectionResult(y.Id, y.SectionType, y.Title, y.Subtitle, y.ContentMarkdown, y.ContentJson.RootElement.Clone(), y.DisplayOrder)).ToList();
        var media = await db.ProjectMedia.AsNoTracking().Where(y => y.ProjectId == x.Id).OrderBy(y => y.DisplayOrder).ThenBy(y => y.Id).Select(y => new PublicProjectMediaResult(y.Id, y.MediaRole, y.MediaAsset.PublicUrl, y.MediaAsset.AltText, y.Caption, y.DisplayOrder)).ToListAsync(ct);
        return new(x.Id, x.Slug, x.Title, x.Subtitle, x.ShortDescription, x.OverviewMarkdown, x.Role, x.TeamSize, x.StartDate, x.EndDate, x.Status, x.GithubUrl, x.LiveUrl, x.ThumbnailMedia?.PublicUrl, new(x.SeoTitle, x.SeoDescription), tech, sections, media);
    }
}
