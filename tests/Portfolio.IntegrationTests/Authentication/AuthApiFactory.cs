using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Auth;
using Portfolio.Application.Features.Auth.GetCurrentAdmin;
using Portfolio.Application.Features.Auth.Login;
using Portfolio.Application.Features.Auth.Logout;
using Portfolio.Application.Features.Auth.Refresh;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Projects;
using Portfolio.Application.Features.ContactMessages;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.Dashboard;
using Portfolio.Application.Features.SiteSettings;

namespace Portfolio.IntegrationTests.Authentication;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public static readonly Guid AdminId = Guid.Parse("7a525da9-df4d-4f30-a9da-a274d239403d");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Portfolio.Tests",
                ["Jwt:Audience"] = "Portfolio.Admin.Tests",
                ["Jwt:SecretKey"] = "integration-test-key-with-at-least-32-characters",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "7"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<LoginCommand, LoginResult>>();
            services.RemoveAll<IRequestHandler<RefreshCommand, RefreshResult>>();
            services.RemoveAll<IRequestHandler<LogoutCommand, bool>>();
            services.RemoveAll<IRequestHandler<GetCurrentAdminQuery, CurrentAdminResult>>();
            services.RemoveAll<IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>>();
            services.RemoveAll<IRequestHandler<GetPublicProjectsQuery, IReadOnlyCollection<PublicProjectListItem>>>();
            services.RemoveAll<IRequestHandler<GetPublicProjectBySlugQuery, PublicProjectDetail>>();
            services.RemoveAll<IRequestHandler<SubmitContactMessageCommand, ContactSubmissionResult>>();
            services.RemoveAll<IRequestHandler<GetSiteSettingsQuery, SiteSettingsResult>>();
            services.RemoveAll<IRequestHandler<UpdateSiteSettingsCommand, SiteSettingsResult>>();
            services.RemoveAll<IRequestHandler<GetDashboardQuery, DashboardResult>>();
            services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, FakeLoginHandler>();
            services.AddScoped<IRequestHandler<RefreshCommand, RefreshResult>, FakeRefreshHandler>();
            services.AddScoped<IRequestHandler<LogoutCommand, bool>, FakeLogoutHandler>();
            services.AddScoped<IRequestHandler<GetCurrentAdminQuery, CurrentAdminResult>, FakeCurrentAdminHandler>();
            services.AddScoped<IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>, FakePublicPortfolioHandler>();
            services.AddScoped<IRequestHandler<GetPublicProjectsQuery, IReadOnlyCollection<PublicProjectListItem>>, FakePublicProjectsHandler>();
            services.AddScoped<IRequestHandler<GetPublicProjectBySlugQuery, PublicProjectDetail>, FakePublicProjectDetailHandler>();
            services.AddScoped<IRequestHandler<SubmitContactMessageCommand, ContactSubmissionResult>, FakeContactHandler>();
            services.AddScoped<IRequestHandler<GetSiteSettingsQuery, SiteSettingsResult>, FakeGetSiteSettingsHandler>();
            services.AddScoped<IRequestHandler<UpdateSiteSettingsCommand, SiteSettingsResult>, FakeUpdateSiteSettingsHandler>();
            services.AddScoped<IRequestHandler<GetDashboardQuery, DashboardResult>, FakeDashboardHandler>();
        });
    }

    public sealed class FakeLoginHandler : IRequestHandler<LoginCommand, LoginResult>
    {
        public Task<LoginResult> HandleAsync(LoginCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginResult(
                "test-access-token",
                900,
                new AdminSummary(AdminId, "admin@example.com", "Portfolio Admin"),
                "test-refresh-token",
                DateTimeOffset.UtcNow.AddDays(7)));
    }

    public sealed class FakeRefreshHandler : IRequestHandler<RefreshCommand, RefreshResult>
    {
        public Task<RefreshResult> HandleAsync(RefreshCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RefreshResult(
                "rotated-access-token",
                900,
                "rotated-refresh-token",
                DateTimeOffset.UtcNow.AddDays(7)));
    }

    public sealed class FakeLogoutHandler : IRequestHandler<LogoutCommand, bool>
    {
        public Task<bool> HandleAsync(LogoutCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    public sealed class FakeCurrentAdminHandler : IRequestHandler<GetCurrentAdminQuery, CurrentAdminResult>
    {
        public Task<CurrentAdminResult> HandleAsync(
            GetCurrentAdminQuery request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CurrentAdminResult(
                AdminId,
                "admin@example.com",
                "Portfolio Admin",
                new DateTimeOffset(2026, 8, 27, 4, 0, 0, TimeSpan.Zero)));
    }

    public sealed class FakePublicPortfolioHandler : IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>
    {
        public Task<PortfolioHomeResult> HandleAsync(GetPublicPortfolioQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PortfolioHomeResult(
                new PublicProfileResult("Owner", null, null, null, null, null, null, null,
                    null, null, null, null, null),
                [], [], [], [], [], [], [], []));
    }

    public sealed class FakePublicProjectsHandler : IRequestHandler<GetPublicProjectsQuery, IReadOnlyCollection<PublicProjectListItem>>
    {
        public Task<IReadOnlyCollection<PublicProjectListItem>> HandleAsync(GetPublicProjectsQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<PublicProjectListItem>>([new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "project", "Project", null, null, null, "ACTIVE", true, null, [])]);
    }

    public sealed class FakePublicProjectDetailHandler : IRequestHandler<GetPublicProjectBySlugQuery, PublicProjectDetail>
    {
        public Task<PublicProjectDetail> HandleAsync(GetPublicProjectBySlugQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicProjectDetail(Guid.Parse("11111111-1111-1111-1111-111111111111"), request.Slug, "Project", null, null, null, null, null, null, null, "ACTIVE", null, null, null, new(null, null), [], [], []));
    }

    public sealed class FakeContactHandler : IRequestHandler<SubmitContactMessageCommand, ContactSubmissionResult>
    {
        public Task<ContactSubmissionResult> HandleAsync(SubmitContactMessageCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContactSubmissionResult(Guid.Parse("33333333-3333-3333-3333-333333333333"), "NEW"));
    }

    public sealed class FakeGetSiteSettingsHandler : IRequestHandler<GetSiteSettingsQuery, SiteSettingsResult>
    { public Task<SiteSettingsResult> HandleAsync(GetSiteSettingsQuery request, CancellationToken cancellationToken = default) => Task.FromResult(new SiteSettingsResult("Portfolio", null, true, true, true, true, false, null, null)); }
    public sealed class FakeUpdateSiteSettingsHandler : IRequestHandler<UpdateSiteSettingsCommand, SiteSettingsResult>
    { public Task<SiteSettingsResult> HandleAsync(UpdateSiteSettingsCommand request, CancellationToken cancellationToken = default) => Task.FromResult(new SiteSettingsResult(request.SiteName, request.FooterText, request.ShowAvailability, request.EnableContactForm, request.ShowDownloadCv, request.ShowJourney, request.ShowAiAgent, request.DefaultSeoTitle, request.DefaultSeoDescription)); }
    public sealed class FakeDashboardHandler : IRequestHandler<GetDashboardQuery, DashboardResult>
    { public Task<DashboardResult> HandleAsync(GetDashboardQuery request, CancellationToken cancellationToken = default) => Task.FromResult(new DashboardResult(1, 2, 3, 4, 5, new(6, 7, 8), 9, [])); }
}
