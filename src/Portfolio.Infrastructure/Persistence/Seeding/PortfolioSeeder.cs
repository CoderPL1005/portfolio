using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Seeding;

public sealed class PortfolioSeeder(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(string seedFilePath, CancellationToken cancellationToken = default)
    {
        var seed = await PortfolioSeedData.LoadAsync(seedFilePath, cancellationToken);

        if (!await dbContext.Profiles.AnyAsync(cancellationToken))
        {
            var item = seed.Profile;
            dbContext.Profiles.Add(new Profile
            {
                Id = Guid.NewGuid(), SingletonKey = 1, FullName = item.FullName,
                ProfessionalTitle = item.ProfessionalTitle, SecondaryTitle = item.SecondaryTitle,
                HeroHeadline = item.HeroHeadline, HeroSummary = item.HeroSummary,
                AboutMarkdown = item.AboutMarkdown, Email = item.Email, Phone = item.Phone,
                Location = item.Location, University = item.University, Major = item.Major,
                AvailabilityStatus = item.AvailabilityStatus, IsPublished = item.IsPublished
            });
        }

        var technologies = (await dbContext.Technologies.ToListAsync(cancellationToken))
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var item in seed.Technologies.Where(item => !technologies.ContainsKey(item.Name)))
        {
            var entity = new Technology
            {
                Id = Guid.NewGuid(), Name = item.Name, Category = item.Category, IconKey = item.IconKey,
                WebsiteUrl = item.WebsiteUrl, DisplayOrder = item.DisplayOrder, IsActive = item.IsActive
            };
            technologies.Add(entity.Name, entity);
            dbContext.Technologies.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var experienceKeys = (await dbContext.Experiences.AsNoTracking().ToListAsync(cancellationToken))
            .Select(item => ExperienceKey(item.CompanyName, item.RoleTitle, item.StartDate))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in seed.Experiences.Where(item => experienceKeys.Add(ExperienceKey(item.CompanyName, item.RoleTitle, item.StartDate))))
        {
            var entity = new Experience
            {
                Id = Guid.NewGuid(), CompanyName = item.CompanyName, RoleTitle = item.RoleTitle,
                Location = item.Location, StartDate = item.StartDate, EndDate = item.EndDate,
                IsCurrent = item.IsCurrent, Summary = item.Summary,
                ResponsibilitiesMarkdown = item.ResponsibilitiesMarkdown, CompanyUrl = item.CompanyUrl,
                DisplayOrder = item.DisplayOrder, IsPublished = item.IsPublished
            };
            dbContext.Experiences.Add(entity);
            dbContext.ExperienceTechnologies.AddRange(item.Technologies.Select((name, index) =>
                new ExperienceTechnology { ExperienceId = entity.Id, TechnologyId = technologies[name].Id, DisplayOrder = index }));
        }

        var projectSlugs = (await dbContext.Projects.AsNoTracking().Select(item => item.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in seed.Projects.Where(item => projectSlugs.Add(item.Slug)))
        {
            var entity = new Project
            {
                Id = Guid.NewGuid(), Slug = item.Slug, Title = item.Title, Subtitle = item.Subtitle,
                ShortDescription = item.ShortDescription, OverviewMarkdown = item.OverviewMarkdown,
                Role = item.Role, TeamSize = item.TeamSize, StartDate = item.StartDate, EndDate = item.EndDate,
                Status = item.Status, GithubUrl = item.GithubUrl, LiveUrl = item.LiveUrl,
                Featured = item.Featured, IsPublished = item.IsPublished, DisplayOrder = item.DisplayOrder,
                SeoTitle = item.SeoTitle, SeoDescription = item.SeoDescription
            };
            dbContext.Projects.Add(entity);
            dbContext.ProjectTechnologies.AddRange(item.Technologies.Select((name, index) =>
                new ProjectTechnology { ProjectId = entity.Id, TechnologyId = technologies[name].Id, DisplayOrder = index }));
            dbContext.ProjectSections.AddRange(item.Sections.Select(section => new ProjectSection
            {
                Id = Guid.NewGuid(), ProjectId = entity.Id, SectionType = section.SectionType,
                Title = section.Title, Subtitle = section.Subtitle, ContentMarkdown = section.ContentMarkdown,
                ContentJson = JsonDocument.Parse(section.Content.GetRawText()),
                DisplayOrder = section.DisplayOrder, IsVisible = section.IsVisible
            }));
        }

        var skillKeys = (await dbContext.Skills.AsNoTracking().ToListAsync(cancellationToken))
            .Select(item => CompositeKey(item.Name, item.Category)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.Skills.AddRange(seed.Skills
            .Where(item => skillKeys.Add(CompositeKey(item.Name, item.Category)))
            .Select(item => new Skill
            {
                Id = Guid.NewGuid(), Name = item.Name, Category = item.Category,
                ExperienceLevel = item.ExperienceLevel, Description = item.Description,
                TechnologyId = item.Technology is null ? null : technologies[item.Technology].Id,
                DisplayOrder = item.DisplayOrder, IsPublished = item.IsPublished
            }));

        var educationKeys = (await dbContext.Educations.AsNoTracking().ToListAsync(cancellationToken))
            .Select(item => CompositeKey(item.Institution, item.FieldOfStudy)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.Educations.AddRange(seed.Educations
            .Where(item => educationKeys.Add(CompositeKey(item.Institution, item.FieldOfStudy)))
            .Select(item => new Education
            {
                Id = Guid.NewGuid(), Institution = item.Institution, Degree = item.Degree,
                FieldOfStudy = item.FieldOfStudy, StartDate = item.StartDate, EndDate = item.EndDate,
                Description = item.Description, Location = item.Location,
                DisplayOrder = item.DisplayOrder, IsPublished = item.IsPublished
            }));

        var trainingKeys = (await dbContext.Trainings.AsNoTracking().ToListAsync(cancellationToken))
            .Select(item => CompositeKey(item.Title, item.Provider)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.Trainings.AddRange(seed.Trainings
            .Where(item => trainingKeys.Add(CompositeKey(item.Title, item.Provider)))
            .Select(item => new Training
            {
                Id = Guid.NewGuid(), Title = item.Title, Provider = item.Provider, Description = item.Description,
                StartDate = item.StartDate, EndDate = item.EndDate, CredentialUrl = item.CredentialUrl,
                DisplayOrder = item.DisplayOrder, IsPublished = item.IsPublished
            }));

        var certificateKeys = (await dbContext.Certificates.AsNoTracking().ToListAsync(cancellationToken))
            .Select(CertificateKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.Certificates.AddRange(seed.Certificates
            .Where(item => certificateKeys.Add(CertificateKey(item)))
            .Select(item => new Certificate
            {
                Id = Guid.NewGuid(), Name = item.Name, Issuer = item.Issuer, IssuedAt = item.IssuedAt,
                ExpiresAt = item.ExpiresAt, CredentialId = item.CredentialId, CredentialUrl = item.CredentialUrl,
                DisplayOrder = item.DisplayOrder, IsPublished = item.IsPublished
            }));

        var journeyKeys = (await dbContext.JourneyItems.AsNoTracking().ToListAsync(cancellationToken))
            .Select(item => item.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.JourneyItems.AddRange(seed.Journey.Where(item => journeyKeys.Add(item.Title)).Select(item => new JourneyItem
        {
            Id = Guid.NewGuid(), Title = item.Title, Subtitle = item.Subtitle, Description = item.Description,
            OccurredAt = item.OccurredAt, IconKey = item.IconKey, DisplayOrder = item.DisplayOrder,
            IsPublished = item.IsPublished
        }));

        var socialPlatforms = (await dbContext.SocialLinks.AsNoTracking().Select(item => item.Platform).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.SocialLinks.AddRange(seed.SocialLinks.Where(item => socialPlatforms.Add(item.Platform)).Select(item => new SocialLink
        {
            Id = Guid.NewGuid(), Platform = item.Platform, Label = item.Label, Url = item.Url,
            IconKey = item.IconKey, DisplayOrder = item.DisplayOrder, IsVisible = item.IsVisible
        }));

        var settingKeys = (await dbContext.SiteSettings.AsNoTracking().Select(item => item.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in seed.SiteSettings.EnumerateObject().Where(property => settingKeys.Add(property.Name)))
        {
            dbContext.SiteSettings.Add(new SiteSetting
            {
                Key = property.Name,
                Value = JsonDocument.Parse(property.Value.GetRawText())
            });
        }

        if (!await dbContext.AgentSettings.AnyAsync(
                item => item.Name.ToLower() == seed.AgentSettings.Name.ToLower(), cancellationToken))
        {
            var item = seed.AgentSettings;
            dbContext.AgentSettings.Add(new AgentSetting
            {
                Id = Guid.NewGuid(), Name = item.Name, Enabled = item.Enabled, Provider = item.Provider,
                ModelName = item.ModelName, EmbeddingProvider = item.EmbeddingProvider,
                EmbeddingModel = item.EmbeddingModel, EmbeddingDimensions = item.EmbeddingDimensions,
                SystemPrompt = item.SystemPrompt, WelcomeMessage = item.WelcomeMessage,
                FallbackMessage = item.FallbackMessage, MaxContextChunks = item.MaxContextChunks,
                MinimumSimilarity = item.MinimumSimilarity, Temperature = item.Temperature
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static string ExperienceKey(string company, string role, DateOnly startDate) => $"{company}|{role}|{startDate:O}";
    public static string CompositeKey(string first, string? second) => $"{first}|{second}";
    public static string CertificateKey(Certificate item) => item.CredentialId ?? CompositeKey(item.Name, item.Issuer);
    public static string CertificateKey(CertificateSeed item) => item.CredentialId ?? CompositeKey(item.Name, item.Issuer);
}
