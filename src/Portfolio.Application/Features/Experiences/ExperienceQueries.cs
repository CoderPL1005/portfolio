using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Experiences;

public sealed record GetExperiencesQuery(string? Search)
    : IRequest<IReadOnlyCollection<AdminExperienceResult>>;
public sealed record GetExperienceQuery(Guid Id) : IRequest<AdminExperienceResult>;

public sealed class GetExperiencesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExperiencesQuery, IReadOnlyCollection<AdminExperienceResult>>
{
    public async Task<IReadOnlyCollection<AdminExperienceResult>> HandleAsync(
        GetExperiencesQuery request,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Experiences.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(item =>
                item.CompanyName.ToLower().Contains(search) || item.RoleTitle.ToLower().Contains(search));
        }

        query = query.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id);

        return await ExperienceProjection.Admin(query, dbContext)
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetExperienceQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExperienceQuery, AdminExperienceResult>
{
    public async Task<AdminExperienceResult> HandleAsync(
        GetExperienceQuery request,
        CancellationToken cancellationToken = default) =>
        await ExperienceProjection.Admin(
                dbContext.Experiences.AsNoTracking().Where(item => item.Id == request.Id),
                dbContext)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("EXPERIENCE_NOT_FOUND", "The experience was not found.");
}

internal static class ExperienceProjection
{
    public static IQueryable<AdminExperienceResult> Admin(
        IQueryable<Portfolio.Domain.Entities.Experience> query,
        IApplicationDbContext dbContext) =>
        query.Select(item => new AdminExperienceResult(
            item.Id, item.CompanyName, item.RoleTitle, item.Location, item.StartDate,
            item.EndDate, item.IsCurrent, item.Summary, item.ResponsibilitiesMarkdown,
            item.CompanyUrl, item.DisplayOrder, item.IsPublished,
            dbContext.ExperienceTechnologies
                .Where(link => link.ExperienceId == item.Id)
                .OrderBy(link => link.DisplayOrder).ThenBy(link => link.TechnologyId)
                .Select(link => new TechnologySummary(
                    link.Technology.Id, link.Technology.Name,
                    link.Technology.Category, link.Technology.IconKey)).ToList()));
}
