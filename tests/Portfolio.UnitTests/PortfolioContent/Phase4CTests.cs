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
