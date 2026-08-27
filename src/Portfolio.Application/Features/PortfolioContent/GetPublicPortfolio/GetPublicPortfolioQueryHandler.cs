using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
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

        var experiences = await dbContext.Experiences.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicExperienceResult(
                item.Id, item.CompanyName, item.RoleTitle, item.Location, item.StartDate,
                item.EndDate, item.IsCurrent, item.Summary, item.ResponsibilitiesMarkdown,
                item.CompanyUrl,
                dbContext.ExperienceTechnologies
                    .Where(link => link.ExperienceId == item.Id && link.Technology.IsActive)
                    .OrderBy(link => link.DisplayOrder).ThenBy(link => link.TechnologyId)
                    .Select(link => new TechnologySummary(
                        link.Technology.Id, link.Technology.Name, link.Technology.Category,
                        link.Technology.IconKey)).ToList()))
            .ToListAsync(cancellationToken);

        var educations = await dbContext.Educations.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicEducationResult(
                item.Id, item.Institution, item.Degree, item.FieldOfStudy, item.StartDate,
                item.EndDate, item.Description, item.Location))
            .ToListAsync(cancellationToken);
        var trainings = await dbContext.Trainings.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicTrainingResult(
                item.Id, item.Title, item.Provider, item.Description, item.StartDate,
                item.EndDate, item.CredentialUrl))
            .ToListAsync(cancellationToken);
        var certificates = await dbContext.Certificates.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicCertificateResult(
                item.Id, item.Name, item.Issuer, item.IssuedAt, item.ExpiresAt,
                item.CredentialId, item.CredentialUrl,
                item.CertificateMedia == null ? null : item.CertificateMedia.PublicUrl))
            .ToListAsync(cancellationToken);

        var featuredProjects = await new GetPublicProjectsQueryHandler(dbContext)
            .HandleAsync(new GetPublicProjectsQuery(true), cancellationToken);
        var skills = await dbContext.Skills.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicSkillResult(
                item.Id, item.Name, item.Category, item.ExperienceLevel, item.Description,
                item.Technology != null && item.Technology.IsActive ? item.TechnologyId : null))
            .ToListAsync(cancellationToken);

        return new PortfolioHomeResult(
            profile, experiences, featuredProjects, skills, educations, trainings, certificates, [], []);
    }
}
