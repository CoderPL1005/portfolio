using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Dashboard;
using Portfolio.Application.Features.Journey;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Application.Features.SiteSettings;
using Portfolio.Application.Features.SocialLinks;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class Phase4CTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Journey_crud_reorder_validation_and_not_found_work()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var create = new CreateJourneyItemCommandHandler(db, new FixedTimeProvider(Now));
        var second = await create.HandleAsync(new("Second", null, null, null, null, 2, false)); var first = await create.HandleAsync(new("First", null, null, null, "code", 1, true));
        var list = await new GetJourneyItemsQueryHandler(db).HandleAsync(new()); var updated = await new UpdateJourneyItemCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(second.Id, "Updated", null, null, null, null, 2, true));
        await new ReorderJourneyItemsCommandHandler(db).HandleAsync(new([new(second.Id, 0), new(first.Id, 1)])); var invalid = await new CreateJourneyItemCommandValidator().ValidateAsync(new("", new string('x', 256), null, null, new string('x', 101), -1, true));
        Assert.Equal(new[] { first.Id, second.Id }, list.Select(x => x.Id)); Assert.Equal("Updated", updated.Title); Assert.True(invalid.Count >= 4);
        await new DeleteJourneyItemCommandHandler(db).HandleAsync(new(second.Id)); await Assert.ThrowsAsync<NotFoundException>(() => new GetJourneyItemQueryHandler(db).HandleAsync(new(second.Id)));
    }

    [Fact]
    public async Task Social_link_crud_reorder_url_validation_and_not_found_work()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var create = new CreateSocialLinkCommandHandler(db, new FixedTimeProvider(Now));
        var second = await create.HandleAsync(new("Email", "Email", "mailto:test@example.com", "mail", 2, false)); var first = await create.HandleAsync(new("GitHub", "GitHub", "https://github.com/example", "github", 1, true));
        var list = await new GetSocialLinksQueryHandler(db).HandleAsync(new());
        var invalid = await new CreateSocialLinkCommandValidator().ValidateAsync(new("", null, "javascript:alert(1)", null, -1, true)); await new ReorderSocialLinksCommandHandler(db).HandleAsync(new([new(second.Id, 0), new(first.Id, 1)]));
        Assert.Equal(new[] { first.Id, second.Id }, list.Select(x => x.Id)); Assert.Contains(invalid, x => x.PropertyName == "url");
        await new DeleteSocialLinkCommandHandler(db).HandleAsync(new(second.Id)); await Assert.ThrowsAsync<NotFoundException>(() => new UpdateSocialLinkCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(new(second.Id, "Email", null, "mailto:a@b.com", null, 0, true)));
    }

    [Fact]
    public async Task Social_links_allow_duplicate_platforms_for_create_admin_list_public_portfolio_and_update()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        db.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = true });
        await db.SaveChangesAsync();
        var create = new CreateSocialLinkCommandHandler(db, new FixedTimeProvider(Now));

        var first = await create.HandleAsync(new("GitHub", "CoderPL1005", "https://github.com/CoderPL1005", "github", 1, true));
        var second = await create.HandleAsync(new("GitHub", "PhucND3009", "https://github.com/PhucND3009", "github", 2, true));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.SocialLinks.CountAsync(x => x.Platform == "GitHub"));
        var adminList = await new GetSocialLinksQueryHandler(db).HandleAsync(new());
        Assert.Equal(new[] { first.Id, second.Id }, adminList.Select(x => x.Id));

        var updated = await new UpdateSocialLinkCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(new(first.Id, "GitHub", "Primary GitHub", "https://github.com/CoderPL1005", "github", 1, true));

        Assert.Equal("Primary GitHub", updated.Label);
        Assert.Equal(2, await db.SocialLinks.CountAsync(x => x.Platform == "GitHub"));
        var publicPortfolio = await new GetPublicPortfolioQueryHandler(db).HandleAsync(new());
        Assert.Equal(new[] { first.Id, second.Id }, publicPortfolio.SocialLinks.Select(x => x.Id));
        Assert.Equal(new[] { "https://github.com/CoderPL1005", "https://github.com/PhucND3009" }, publicPortfolio.SocialLinks.Select(x => x.Url));
    }

    [Fact]
    public async Task Public_portfolio_filters_and_orders_journey_and_social_links()
    {
        await using var db = PublicPortfolioTests.CreateContext(); db.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = true });
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001"); var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        db.JourneyItems.AddRange(Journey(firstId, "First", 1, true), Journey(secondId, "Second", 1, true), Journey(Guid.NewGuid(), "Hidden", 0, false));
        db.SocialLinks.AddRange(Social(firstId, "GitHub", 1, true), Social(secondId, "LinkedIn", 1, true), Social(Guid.NewGuid(), "Hidden", 0, false)); await db.SaveChangesAsync();
        var result = await new GetPublicPortfolioQueryHandler(db).HandleAsync(new());
        Assert.Equal(new[] { "First", "Second" }, result.Journey.Select(x => x.Title)); Assert.Equal(new[] { "GitHub", "LinkedIn" }, result.SocialLinks.Select(x => x.Platform)); Assert.DoesNotContain(result.Journey.First().GetType().GetProperties(), x => x.Name == "IsPublished"); Assert.DoesNotContain(result.SocialLinks.First().GetType().GetProperties(), x => x.Name == "IsVisible");
    }

    [Fact]
    public async Task Public_journey_aggregates_published_sources_with_stable_ids_mappings_and_chronology()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        db.Profiles.Add(new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Owner", IsPublished = true });
        var sharedSourceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var manualId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        db.Educations.AddRange(
            new Education { Id = sharedSourceId, Institution = "University", Degree = "BSc", FieldOfStudy = "Software Engineering", StartDate = new DateOnly(2020, 1, 1), EndDate = null, Description = "Education description", IsPublished = true },
            new Education { Id = Guid.NewGuid(), Institution = "Hidden education", IsPublished = false });
        db.Experiences.AddRange(
            new Experience { Id = Guid.NewGuid(), CompanyName = "Company", RoleTitle = "Engineer", StartDate = new DateOnly(2021, 2, 1), EndDate = new DateOnly(2021, 12, 1), Summary = "Experience summary", IsPublished = true },
            new Experience { Id = Guid.NewGuid(), CompanyName = "Hidden company", RoleTitle = "Hidden experience", StartDate = new DateOnly(2019, 1, 1), IsPublished = false });
        db.Projects.AddRange(
            new Project { Id = sharedSourceId, Slug = "published-project", Title = "Published project", Subtitle = "Project subtitle", Role = "Project role", ShortDescription = "Project description", StartDate = new DateOnly(2022, 3, 1), EndDate = new DateOnly(2022, 8, 1), Status = "COMPLETED", IsPublished = true },
            new Project { Id = Guid.NewGuid(), Slug = "fallback-project", Title = "Fallback project", Subtitle = "Fallback subtitle", ShortDescription = "Fallback description", StartDate = new DateOnly(2022, 4, 1), Status = "ACTIVE", IsPublished = true },
            new Project { Id = Guid.NewGuid(), Slug = "hidden-project", Title = "Hidden project", Status = "ACTIVE", IsPublished = false });
        db.Trainings.AddRange(
            new Training { Id = Guid.NewGuid(), Title = "Training", Provider = "Provider", Description = "Training description", StartDate = new DateOnly(2021, 6, 1), EndDate = new DateOnly(2021, 8, 1), IsPublished = true },
            new Training { Id = Guid.NewGuid(), Title = "Hidden training", IsPublished = false });
        db.Certificates.AddRange(
            new Certificate { Id = Guid.NewGuid(), Name = "Certificate", Issuer = "Issuer", IssuedAt = new DateOnly(2023, 4, 1), IsPublished = true },
            new Certificate { Id = Guid.NewGuid(), Name = "Hidden certificate", IsPublished = false });
        db.JourneyItems.AddRange(
            new JourneyItem { Id = manualId, Title = "Foundation", OccurredAt = null, DisplayOrder = 1, IsPublished = true },
            new JourneyItem { Id = Guid.NewGuid(), Title = "Manual launch", Subtitle = "Custom milestone", Description = "Manual description", OccurredAt = new DateOnly(2022, 3, 1), DisplayOrder = 2, IsPublished = true },
            new JourneyItem { Id = Guid.NewGuid(), Title = "Hidden manual", IsPublished = false });
        await db.SaveChangesAsync();
        var persistedManualCount = await db.JourneyItems.CountAsync();
        var handler = new GetPublicPortfolioQueryHandler(db);

        var first = await handler.HandleAsync(new());
        var second = await handler.HandleAsync(new());
        var adminHandler = new GetAdminJourneyTimelineQueryHandler(db);
        var adminFirst = await adminHandler.HandleAsync(new());
        var adminSecond = await adminHandler.HandleAsync(new());

        Assert.Equal(
            ["BSc", "Engineer", "Training", "Published project", "Fallback project", "Foundation", "Manual launch", "Certificate"],
            first.Journey.Select(item => item.Title));
        Assert.DoesNotContain(first.Journey, item => item.Title.StartsWith("Hidden", StringComparison.Ordinal));
        Assert.Equal(first.Journey.Select(item => item.Id), second.Journey.Select(item => item.Id));
        Assert.Equal(first.Journey.Select(item => item.Id), adminFirst.Select(item => item.Id));
        Assert.Equal(first.Journey.Select(item => item.Title), adminFirst.Select(item => item.Title));
        Assert.Equal(adminFirst.Select(item => item.Id), adminSecond.Select(item => item.Id));
        Assert.Equal(first.Journey.Count, first.Journey.Select(item => item.Id).Distinct().Count());
        Assert.Equal(manualId, first.Journey.Single(item => item.Title == "Foundation").Id);
        Assert.NotEqual(
            first.Journey.Single(item => item.Title == "BSc").Id,
            first.Journey.Single(item => item.Title == "Published project").Id);

        var education = first.Journey.Single(item => item.Title == "BSc");
        Assert.Equal("University", education.Subtitle); Assert.Equal("Education description", education.Description); Assert.Equal(new DateOnly(2020, 1, 1), education.OccurredAt); Assert.Equal(education.OccurredAt, education.StartAt); Assert.Null(education.EndAt); Assert.True(education.IsOngoing); Assert.Equal("PERIOD", education.TimelineKind);
        var experience = first.Journey.Single(item => item.Title == "Engineer");
        Assert.Equal("Company", experience.Subtitle); Assert.Equal("Experience summary", experience.Description); Assert.Equal(new DateOnly(2021, 2, 1), experience.StartAt); Assert.Equal(new DateOnly(2021, 12, 1), experience.EndAt); Assert.False(experience.IsOngoing); Assert.Equal("PERIOD", experience.TimelineKind);
        var project = first.Journey.Single(item => item.Title == "Published project");
        Assert.Equal("Project role", project.Subtitle); Assert.Equal("Project description", project.Description); Assert.Equal(new DateOnly(2022, 3, 1), project.StartAt); Assert.Equal(new DateOnly(2022, 8, 1), project.EndAt); Assert.False(project.IsOngoing); Assert.Equal("PERIOD", project.TimelineKind);
        var ongoingProject = first.Journey.Single(item => item.Title == "Fallback project");
        Assert.Equal("Fallback subtitle", ongoingProject.Subtitle); Assert.Null(ongoingProject.EndAt); Assert.True(ongoingProject.IsOngoing);
        var training = first.Journey.Single(item => item.Title == "Training");
        Assert.Equal("Provider", training.Subtitle); Assert.Equal("Training description", training.Description); Assert.Equal(new DateOnly(2021, 6, 1), training.StartAt); Assert.Equal(new DateOnly(2021, 8, 1), training.EndAt); Assert.False(training.IsOngoing); Assert.Equal("PERIOD", training.TimelineKind);
        var certificate = first.Journey.Single(item => item.Title == "Certificate");
        Assert.Equal("Issuer", certificate.Subtitle); Assert.Null(certificate.Description); Assert.Equal(new DateOnly(2023, 4, 1), certificate.StartAt); Assert.Null(certificate.EndAt); Assert.False(certificate.IsOngoing); Assert.Equal("POINT", certificate.TimelineKind);
        var manual = first.Journey.Single(item => item.Title == "Manual launch");
        Assert.Equal("Custom milestone", manual.Subtitle); Assert.Equal("Manual description", manual.Description); Assert.Equal(manual.OccurredAt, manual.StartAt); Assert.Null(manual.EndAt); Assert.False(manual.IsOngoing); Assert.Equal("POINT", manual.TimelineKind);
        Assert.Equal(
            ["EDUCATION", "EXPERIENCE", "TRAINING", "PROJECT", "PROJECT", "MANUAL", "MANUAL", "CERTIFICATE"],
            adminFirst.Select(item => item.SourceType));
        Assert.Equal(manualId, adminFirst.Single(item => item.Title == "Foundation").SourceId);
        Assert.True(adminFirst.Single(item => item.Title == "Foundation").IsManual);
        Assert.False(adminFirst.Single(item => item.Title == "Published project").IsManual);
        Assert.Equal(sharedSourceId, adminFirst.Single(item => item.Title == "BSc").SourceId);
        Assert.Equal((await db.Experiences.SingleAsync(item => item.RoleTitle == "Engineer")).Id,
            adminFirst.Single(item => item.Title == "Engineer").SourceId);
        Assert.Equal(sharedSourceId, adminFirst.Single(item => item.Title == "Published project").SourceId);
        Assert.Equal((await db.Trainings.SingleAsync(item => item.Title == "Training")).Id,
            adminFirst.Single(item => item.Title == "Training").SourceId);
        Assert.Equal((await db.Certificates.SingleAsync(item => item.Name == "Certificate")).Id,
            adminFirst.Single(item => item.Title == "Certificate").SourceId);
        Assert.Equal(first.Journey.Select(item => new { item.StartAt, item.EndAt, item.IsOngoing, item.TimelineKind }),
            adminFirst.Select(item => new { item.StartAt, item.EndAt, item.IsOngoing, item.TimelineKind }));
        Assert.Equal(persistedManualCount, await db.JourneyItems.CountAsync());
        Assert.Equal(["Id", "Title", "Subtitle", "Description", "OccurredAt", "IconKey", "SourceType", "SourceId", "StartAt", "EndAt", "IsOngoing", "TimelineKind"],
            first.Journey.First().GetType().GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Site_settings_map_typed_values_to_only_approved_json_rows()
    {
        await using var db = PublicPortfolioTests.CreateContext(); db.SiteSettings.Add(new SiteSetting { Key = "unexpected", Value = JsonDocument.Parse("{\"unsafe\":true}") }); await db.SaveChangesAsync();
        var command = new UpdateSiteSettingsCommand("Portfolio", "Footer", true, false, true, false, "SEO", "Description"); var result = await new UpdateSiteSettingsCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(command); var loaded = await new GetSiteSettingsQueryHandler(db).HandleAsync(new());
        Assert.Equal("Portfolio", result.SiteName); Assert.Equal(result, loaded); Assert.Equal(9, await db.SiteSettings.CountAsync()); Assert.Equal(JsonValueKind.Object, (await db.SiteSettings.SingleAsync(x => x.Key == "unexpected")).Value.RootElement.ValueKind);
    }

    [Fact]
    public async Task Site_settings_validation_enforces_typed_string_limits()
    {
        var failures = await new UpdateSiteSettingsCommandValidator().ValidateAsync(new("", new string('x', 501), true, true, true, true, new string('x', 256), new string('x', 501)));
        Assert.Equal(4, failures.Count);
    }

    [Fact]
    public async Task Dashboard_uses_persisted_counts_without_fake_analytics()
    {
        await using var db = PublicPortfolioTests.CreateContext(); db.Projects.Add(Project()); db.Experiences.Add(new Experience { Id = Guid.NewGuid(), CompanyName = "Company", RoleTitle = "Role", StartDate = new DateOnly(2026, 1, 1) }); db.Skills.Add(new Skill { Id = Guid.NewGuid(), Name = "Skill", Category = "Backend", ExperienceLevel = "USED" }); db.Certificates.Add(new Certificate { Id = Guid.NewGuid(), Name = "Cert" }); db.KnowledgeDocuments.AddRange(Knowledge("INDEXED"), Knowledge("PENDING"), Knowledge("FAILED"), Knowledge("INDEXING")); db.ChatSessions.Add(new ChatSession { Id = Guid.NewGuid(), PublicSessionId = Guid.NewGuid(), Status = "ACTIVE", Metadata = JsonDocument.Parse("{}") }); await db.SaveChangesAsync();
        var result = await new GetDashboardQueryHandler(db).HandleAsync(new());
        Assert.Equal(1, result.Projects); Assert.Equal(1, result.Experiences); Assert.Equal(1, result.Skills); Assert.Equal(1, result.Certificates); Assert.Equal(1, result.Knowledge.Indexed); Assert.Equal(1, result.Knowledge.Pending); Assert.Equal(1, result.Knowledge.Failed); Assert.Equal(1, result.Conversations); Assert.Equal(4, result.RecentUpdates.Count);
    }

    private static JourneyItem Journey(Guid id, string title, int order, bool published) => new() { Id = id, Title = title, DisplayOrder = order, IsPublished = published };
    private static SocialLink Social(Guid id, string platform, int order, bool visible) => new() { Id = id, Platform = platform, Url = "https://example.com", DisplayOrder = order, IsVisible = visible };
    private static Project Project() => new() { Id = Guid.NewGuid(), Slug = "project", Title = "Project", Status = "ACTIVE" };
    private static KnowledgeDocument Knowledge(string status) => new() { Id = Guid.NewGuid(), SourceType = "PROFILE", SourceKey = Guid.NewGuid().ToString(), Title = "Title", Content = "Content", ContentHash = "hash", Version = 1, Metadata = JsonDocument.Parse("{}"), IndexingStatus = status };
}
