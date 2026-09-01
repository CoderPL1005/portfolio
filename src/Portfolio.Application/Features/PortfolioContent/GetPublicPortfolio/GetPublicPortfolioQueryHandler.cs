using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.Projects;

namespace Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;

public sealed class GetPublicPortfolioQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>
{
    public async Task<PortfolioHomeResult> HandleAsync(
        GetPublicPortfolioQuery request,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.Profiles.AsNoTracking()
            .Where(item => item.SingletonKey == 1 && item.IsPublished)
            .Select(item => new PublicProfileResult(
                item.FullName, item.ProfessionalTitle, item.SecondaryTitle, item.HeroHeadline,
                item.HeroSummary, item.AboutMarkdown, item.Email, item.Location, item.University,
                item.Major, item.AvailabilityStatus,
                item.ProfileImage == null ? null : item.ProfileImage.PublicUrl,
                item.CvMedia == null ? null : item.CvMedia.PublicUrl))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("PROFILE_NOT_FOUND", "The published profile was not found.");

        var timeline = await PublicJourneyTimeline.LoadAsync(dbContext, cancellationToken);

        var featuredProjects = await new GetPublicProjectsQueryHandler(dbContext)
            .HandleAsync(new GetPublicProjectsQuery(true), cancellationToken);
        var technologies = await dbContext.Technologies.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new TechnologySummary(
                item.Id, item.Name, item.Category, item.IconKey))
            .ToListAsync(cancellationToken);
        var skills = await dbContext.Skills.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicSkillResult(
                item.Id, item.Name, item.Category, item.ExperienceLevel, item.Description,
                item.Technology != null && item.Technology.IsActive ? item.TechnologyId : null))
            .ToListAsync(cancellationToken);
        var socialLinks = await dbContext.SocialLinks.AsNoTracking()
            .Where(item => item.IsVisible)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicSocialLinkResult(item.Id, item.Platform, item.Label,
                item.Url, item.IconKey))
            .ToListAsync(cancellationToken);

        return new PortfolioHomeResult(
            profile, timeline.Experiences, featuredProjects, technologies, skills,
            timeline.Educations, timeline.Trainings, timeline.Certificates,
            timeline.PublicItems, socialLinks);
    }
}
