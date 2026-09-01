using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class PublicPortfolioTests
{
    [Fact]
    public async Task Public_aggregate_filters_unpublished_content_and_orders_deterministically()
    {
        await using var context = CreateContext();
        context.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = true });
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        context.Experiences.AddRange(
            Experience(secondId, "second", 1, true), Experience(firstId, "first", 1, true),
            Experience(Guid.NewGuid(), "hidden", 0, false));
        context.Educations.AddRange(Education(secondId, "second", 1, true), Education(firstId, "first", 1, true), Education(Guid.NewGuid(), "hidden", 0, false));
        context.Trainings.AddRange(Training(secondId, "second", 1, true), Training(firstId, "first", 1, true), Training(Guid.NewGuid(), "hidden", 0, false));
        context.Certificates.AddRange(Certificate(secondId, "second", 1, true), Certificate(firstId, "first", 1, true), Certificate(Guid.NewGuid(), "hidden", 0, false));
        var firstTechnologyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondTechnologyId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var laterTechnologyId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var firstActiveTechnology = new Technology { Id = firstTechnologyId, Name = "First active", Category = "Backend", IconKey = "first", DisplayOrder = 1, IsActive = true };
        var secondActiveTechnology = new Technology { Id = secondTechnologyId, Name = "Second active", Category = "Frontend", DisplayOrder = 1, IsActive = true };
        var laterActiveTechnology = new Technology { Id = laterTechnologyId, Name = "Later active", Category = "Database", DisplayOrder = 2, IsActive = true };
        var inactiveTechnology = new Technology { Id = Guid.NewGuid(), Name = "Inactive", Category = "Backend", DisplayOrder = 0, IsActive = false };
        context.Technologies.AddRange(firstActiveTechnology, secondActiveTechnology, laterActiveTechnology, inactiveTechnology);
        var featuredProject = new Project
        {
            Id = Guid.NewGuid(), Slug = "featured", Title = "Featured", Status = "ACTIVE",
            Featured = true, IsPublished = true
        };
        context.Projects.Add(featuredProject);
        context.ProjectTechnologies.AddRange(
            new ProjectTechnology { ProjectId = featuredProject.Id, TechnologyId = firstActiveTechnology.Id, DisplayOrder = 1 },
            new ProjectTechnology { ProjectId = featuredProject.Id, TechnologyId = inactiveTechnology.Id, DisplayOrder = 0 });
        context.ExperienceTechnologies.AddRange(
            new ExperienceTechnology { ExperienceId = firstId, TechnologyId = firstActiveTechnology.Id, DisplayOrder = 1 },
            new ExperienceTechnology { ExperienceId = firstId, TechnologyId = inactiveTechnology.Id, DisplayOrder = 0 });
        await context.SaveChangesAsync();

        var result = await new GetPublicPortfolioQueryHandler(context).HandleAsync(new());

        Assert.Equal(new[] { "first", "second" }, result.Experiences.Select(item => item.CompanyName));
        Assert.Equal(new[] { "first", "second" }, result.Educations.Select(item => item.Institution));
        Assert.Equal(new[] { "first", "second" }, result.Trainings.Select(item => item.Title));
        Assert.Equal(new[] { "first", "second" }, result.Certificates.Select(item => item.Name));
        Assert.Equal(new[] { firstTechnologyId, secondTechnologyId, laterTechnologyId }, result.Technologies.Select(item => item.Id));
        Assert.Equal("first", result.Technologies.First().IconKey);
        Assert.DoesNotContain(result.Technologies, item => item.Name == "Inactive");
        Assert.Equal("First active", result.Experiences.First().Technologies.Single().Name);
        Assert.Equal("First active", result.FeaturedProjects.Single().Technologies.Single().Name);
        Assert.DoesNotContain(result.Profile.GetType().GetProperties(), property => property.Name is "SingletonKey" or "IsPublished");
    }

    [Fact]
    public async Task Public_aggregate_returns_not_found_when_singleton_profile_is_unpublished()
    {
        await using var context = CreateContext();
        context.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = false });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.NotFoundException>(() =>
            new GetPublicPortfolioQueryHandler(context).HandleAsync(new()));

        Assert.Equal("PROFILE_NOT_FOUND", exception.Code);
    }

    internal static ContentTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ContentTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ContentTestDbContext(options);
    }

    private static Experience Experience(Guid id, string name, int order, bool published) => new()
    { Id = id, CompanyName = name, RoleTitle = "Role", StartDate = new DateOnly(2025, 1, 1), DisplayOrder = order, IsPublished = published };
    private static Education Education(Guid id, string name, int order, bool published) => new()
    { Id = id, Institution = name, DisplayOrder = order, IsPublished = published };
    private static Training Training(Guid id, string name, int order, bool published) => new()
    { Id = id, Title = name, DisplayOrder = order, IsPublished = published };
    private static Certificate Certificate(Guid id, string name, int order, bool published) => new()
    { Id = id, Name = name, DisplayOrder = order, IsPublished = published };
}
