using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Projects;
using Portfolio.Application.Features.Skills;
using Portfolio.Application.Features.Technologies;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class Phase4BTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Technology_crud_filters_orders_and_enforces_unique_name()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var handler = new CreateTechnologyCommandHandler(db, new FixedTimeProvider(Now));
        var second = await handler.HandleAsync(new("Angular", "Frontend", null, "https://angular.dev", 1, false));
        var first = await handler.HandleAsync(new("ASP.NET Core", "Backend", "dotnet", null, 0, true));
        var all = await new GetTechnologiesQueryHandler(db).HandleAsync(new(null, null));
        var list = await new GetTechnologiesQueryHandler(db).HandleAsync(new("asp", "backend"));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new("asp.net core", "Other", null, null, 2, true)));
        var invalid = await new CreateTechnologyCommandValidator().ValidateAsync(new("", new string('x', 51), new string('x', 101), "bad", -1, true));
        var updated = await new UpdateTechnologyCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(second.Id, "Angular", "Frontend", null, null, 2, true));
        await new DeleteTechnologyCommandHandler(db).HandleAsync(new(second.Id));
        Assert.Equal(new[] { first.Id, second.Id }, all.Select(x => x.Id)); Assert.Equal(first.Id, list.Single().Id); Assert.True(updated.IsActive); Assert.Equal("TECHNOLOGY_NAME_EXISTS", conflict.Code); Assert.True(invalid.Count >= 5); Assert.False(await db.Technologies.AnyAsync(x => x.Id == second.Id));
    }

    [Fact]
    public async Task Technology_in_use_is_not_deleted()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var technology = Technology("Used", true); db.Technologies.Add(technology); db.Skills.Add(new Skill { Id = Guid.NewGuid(), Name = "Skill", Category = "Backend", ExperienceLevel = "USED", TechnologyId = technology.Id }); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => new DeleteTechnologyCommandHandler(db).HandleAsync(new(technology.Id)));
        Assert.Equal("TECHNOLOGY_IN_USE", error.Code); Assert.True(await db.Technologies.AnyAsync(x => x.Id == technology.Id));
    }

    [Fact]
    public async Task Skill_crud_reorder_validation_and_reference_checks_work()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var technology = Technology("C#", true); db.Technologies.Add(technology); await db.SaveChangesAsync(); var create = new CreateSkillCommandHandler(db, new FixedTimeProvider(Now));
        var skill = await create.HandleAsync(new("ASP.NET Core", "Backend", "used", null, technology.Id, 2, false));
        var other = await create.HandleAsync(new("C#", "Backend", "USED", null, technology.Id, 1, true));
        var listed = await new GetSkillsQueryHandler(db).HandleAsync(new("backend", "USED"));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => create.HandleAsync(new("c#", "BACKEND", "USED", null, null, 3, true)));
        var updated = await new UpdateSkillCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(skill.Id, "ASP.NET Core", "Backend", "LEARNING", "Description", technology.Id, 1, true));
        await new ReorderSkillsCommandHandler(db).HandleAsync(new([new(skill.Id, 0)]));
        var invalid = await new CreateSkillCommandValidator().ValidateAsync(new("", "", "EXPERT", null, null, -1, true));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => create.HandleAsync(new("Missing", "Backend", "USED", null, Guid.NewGuid(), 0, true)));
        Assert.Equal(new[] { other.Id, skill.Id }, listed.Select(x => x.Id)); Assert.Equal("LEARNING", updated.ExperienceLevel); Assert.Equal("SKILL_NAME_EXISTS", conflict.Code); Assert.True(invalid.Count >= 4); Assert.Equal("TECHNOLOGY_NOT_FOUND", missing.Code);
        await new DeleteSkillCommandHandler(db).HandleAsync(new(skill.Id)); await new DeleteSkillCommandHandler(db).HandleAsync(new(other.Id));
    }

    [Fact]
    public async Task Project_crud_replaces_technologies_and_orders_list()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var oldTech = Technology("Old", true); var newTech = Technology("New", true); db.Technologies.AddRange(oldTech, newTech); await db.SaveChangesAsync();
        var create = new CreateProjectCommandHandler(db, new FixedTimeProvider(Now)); var project = await create.HandleAsync(ProjectCreate("project", 2, false, [oldTech.Id]));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => create.HandleAsync(ProjectCreate("project", 3, true, [])));
        var updated = await new UpdateProjectCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(ProjectUpdate(project.Id, "project", 2, true, [newTech.Id]));
        var other = await create.HandleAsync(ProjectCreate("other", 1, true, []));
        var list = await new GetProjectsQueryHandler(db).HandleAsync(new(1, 20, null, null, null, null));
        await new ReorderProjectsCommandHandler(db).HandleAsync(new([new(project.Id, 0), new(other.Id, 1)]));
        Assert.Equal("PROJECT_SLUG_EXISTS", conflict.Code); Assert.Equal(new[] { other.Id, project.Id }, list.Items.Select(x => x.Id)); Assert.Equal(newTech.Id, updated.Technologies.Single().TechnologyId); Assert.False(await db.ProjectTechnologies.AnyAsync(x => x.ProjectId == project.Id && x.TechnologyId == oldTech.Id));
        await new DeleteProjectCommandHandler(db).HandleAsync(new(project.Id));
    }

    [Fact]
    public async Task Failed_project_relationship_validation_does_not_partially_replace_links()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var technology = Technology("Valid", true); db.Technologies.Add(technology); await db.SaveChangesAsync(); var project = await new CreateProjectCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(ProjectCreate("safe", 0, true, [technology.Id]));
        await Assert.ThrowsAsync<NotFoundException>(() => new ReplaceProjectTechnologiesCommandHandler(db).HandleAsync(new(project.Id, [new(Guid.NewGuid(), 0)])));
        Assert.Equal(technology.Id, (await db.ProjectTechnologies.SingleAsync(x => x.ProjectId == project.Id)).TechnologyId);
    }

    [Fact]
    public async Task Project_sections_and_media_support_crud_ordering_and_ownership()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var project = Project("relations", true, true, 0); var media = new MediaAsset { Id = Guid.NewGuid(), StorageKey = "key", PublicUrl = "https://example.com/a.png", FileName = "a.png", MediaType = "IMAGE" }; db.AddRange(project, media); await db.SaveChangesAsync(); using var content = JsonDocument.Parse("{\"items\":[]}");
        var section = await new CreateProjectSectionCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(project.Id, "FEATURES", "Features", null, null, content.RootElement.Clone(), 1, true));
        var otherSection = await new CreateProjectSectionCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(project.Id, "OVERVIEW", "Overview", null, null, content.RootElement.Clone(), 0, true));
        var updatedSection = await new UpdateProjectSectionCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(project.Id, section.Id, "LEARNINGS", "Updated", null, null, content.RootElement.Clone(), 0, true));
        await new ReorderProjectSectionsCommandHandler(db).HandleAsync(new(project.Id, [new(section.Id, 1), new(otherSection.Id, 2)]));
        var link = await new AttachProjectMediaCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(project.Id, media.Id, "SCREENSHOT", "Caption", 1));
        var updatedMedia = await new UpdateProjectMediaCommandHandler(db).HandleAsync(new(project.Id, link.Id, "DIAGRAM", "Updated", 0));
        Assert.Equal("Updated", updatedSection.Title); Assert.Equal(2, (await new GetProjectSectionsQueryHandler(db).HandleAsync(new(project.Id))).Count); Assert.Equal("DIAGRAM", updatedMedia.MediaRole); Assert.Single(await new GetProjectMediaQueryHandler(db).HandleAsync(new(project.Id)));
        await new DeleteProjectSectionCommandHandler(db).HandleAsync(new(project.Id, section.Id)); await new DeleteProjectSectionCommandHandler(db).HandleAsync(new(project.Id, otherSection.Id)); await new DeleteProjectMediaCommandHandler(db).HandleAsync(new(project.Id, link.Id));
    }

    [Fact]
    public async Task Public_projects_filter_visibility_related_content_and_order_deterministically()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var active = Technology("Active", true); var inactive = Technology("Inactive", false); var first = Project("first", true, true, 1, Guid.Parse("00000000-0000-0000-0000-000000000001")); var second = Project("second", true, true, 1, Guid.Parse("00000000-0000-0000-0000-000000000002")); var hidden = Project("hidden", false, true, 0); db.AddRange(active, inactive, first, second, hidden); db.ProjectTechnologies.AddRange(new ProjectTechnology { ProjectId = first.Id, TechnologyId = active.Id }, new ProjectTechnology { ProjectId = first.Id, TechnologyId = inactive.Id, DisplayOrder = 1 }); db.ProjectSections.AddRange(Section(first.Id, true), Section(first.Id, false)); await db.SaveChangesAsync();
        var list = await new GetPublicProjectsQueryHandler(db).HandleAsync(new(null)); var detail = await new GetPublicProjectBySlugQueryHandler(db).HandleAsync(new("first"));
        Assert.Equal(new[] { "first", "second" }, list.Select(x => x.Slug)); Assert.Single(list.First().Technologies); Assert.Single(detail.Sections); await Assert.ThrowsAsync<NotFoundException>(() => new GetPublicProjectBySlugQueryHandler(db).HandleAsync(new("hidden")));
    }

    [Fact]
    public async Task Public_project_detail_maps_media_orders_ties_and_matches_slug_case_insensitively()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var project = Project("public-project", true, false, 0);
        var firstMediaId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondMediaId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var firstAsset = new MediaAsset { Id = Guid.NewGuid(), StorageKey = "first", PublicUrl = "https://example.com/first.png", FileName = "first.png", MediaType = "IMAGE", AltText = "First" };
        var secondAsset = new MediaAsset { Id = Guid.NewGuid(), StorageKey = "second", PublicUrl = "https://example.com/second.png", FileName = "second.png", MediaType = "IMAGE", AltText = "Second" };
        db.AddRange(project, firstAsset, secondAsset);
        db.ProjectMedia.AddRange(
            new ProjectMedia { Id = secondMediaId, ProjectId = project.Id, MediaAssetId = secondAsset.Id, MediaRole = "SCREENSHOT", DisplayOrder = 1 },
            new ProjectMedia { Id = firstMediaId, ProjectId = project.Id, MediaAssetId = firstAsset.Id, MediaRole = "DIAGRAM", Caption = "Architecture", DisplayOrder = 1 });
        await db.SaveChangesAsync();

        var detail = await new GetPublicProjectBySlugQueryHandler(db).HandleAsync(new("PUBLIC-PROJECT"));

        Assert.Equal([firstMediaId, secondMediaId], detail.Media.Select(item => item.Id));
        Assert.Equal("https://example.com/first.png", detail.Media.First().Url);
        Assert.Equal("First", detail.Media.First().AltText);
        Assert.Equal("Architecture", detail.Media.First().Caption);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetPublicProjectBySlugQueryHandler(db).HandleAsync(new("missing")));
    }

    [Fact]
    public async Task Project_validation_rejects_contract_constraint_violations()
    {
        var technologyId = Guid.NewGuid();
        var invalid = new CreateProjectCommand("Bad Slug", "Title", null, null, null, null, -1,
            new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), "INVALID", "not-a-url", null,
            null, true, true, -1, null, null, [technologyId, technologyId]);
        var failures = await new CreateProjectCommandValidator().ValidateAsync(invalid);
        Assert.Contains(failures, x => x.PropertyName == "slug"); Assert.Contains(failures, x => x.PropertyName == "teamSize"); Assert.Contains(failures, x => x.PropertyName == "displayOrder"); Assert.Contains(failures, x => x.PropertyName == "status"); Assert.Contains(failures, x => x.PropertyName == "endDate"); Assert.Contains(failures, x => x.PropertyName == "technologyIds");
    }

    [Fact]
    public async Task Portfolio_aggregate_populates_only_featured_projects_and_published_skills()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        db.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = true });
        db.Projects.AddRange(Project("featured", true, true, 1), Project("regular", true, false, 0), Project("hidden", false, true, 0));
        db.Skills.AddRange(
            new Skill { Id = Guid.NewGuid(), Name = "Visible", Category = "Backend", ExperienceLevel = "USED", IsPublished = true, DisplayOrder = 1 },
            new Skill { Id = Guid.NewGuid(), Name = "Hidden", Category = "Backend", ExperienceLevel = "USED", IsPublished = false, DisplayOrder = 0 });
        await db.SaveChangesAsync();

        var result = await new GetPublicPortfolioQueryHandler(db).HandleAsync(new());

        Assert.Equal("featured", result.FeaturedProjects.Single().Slug);
        Assert.Equal("Visible", result.Skills.Single().Name);
        Assert.Empty(result.Experiences); Assert.Empty(result.Educations); Assert.Empty(result.Journey);
    }

    private static Technology Technology(string name, bool active) => new() { Id = Guid.NewGuid(), Name = name, Category = "Backend", IsActive = active };
    private static Project Project(string slug, bool published, bool featured, int order, Guid? id = null) => new() { Id = id ?? Guid.NewGuid(), Slug = slug, Title = slug, Status = "ACTIVE", IsPublished = published, Featured = featured, DisplayOrder = order };
    private static ProjectSection Section(Guid projectId, bool visible) => new() { Id = Guid.NewGuid(), ProjectId = projectId, SectionType = "FEATURES", ContentJson = JsonDocument.Parse("{}"), IsVisible = visible };
    private static CreateProjectCommand ProjectCreate(string slug, int order, bool published, IReadOnlyCollection<Guid> technologies) => new(slug, slug, null, null, null, null, 1, new DateOnly(2026, 1, 1), null, "ACTIVE", null, null, null, true, published, order, null, null, technologies);
    private static UpdateProjectCommand ProjectUpdate(Guid id, string slug, int order, bool published, IReadOnlyCollection<Guid> technologies) => new(id, slug, slug, null, null, null, null, 1, null, null, "ACTIVE", null, null, null, true, published, order, null, null, technologies);
}
