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
using Portfolio.Application.Features.Media;
using Portfolio.Application.Features.Agent;
using Portfolio.Application.Features.Chat;
using Portfolio.Application.Common.Models;

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
            services.RemoveAll<IRequestHandler<GetMediaQuery, Portfolio.Application.Common.Models.PagedResult<MediaAssetResult>>>();
            services.RemoveAll<IRequestHandler<UploadMediaCommand, MediaAssetResult>>();
            services.RemoveAll<IRequestHandler<UpdateMediaCommand, MediaAssetResult>>();
            services.RemoveAll<IRequestHandler<DeleteMediaCommand, bool>>();
            services.RemoveAll<IRequestHandler<CreateChatSessionCommand,ChatSessionResult>>();services.RemoveAll<IRequestHandler<SendChatMessageCommand,ChatAnswerResult>>();services.RemoveAll<IRequestHandler<SubmitChatFeedbackCommand,Guid>>();
            services.RemoveAll<IRequestHandler<GetAgentSettingsQuery,AgentSettingsResult>>();services.RemoveAll<IRequestHandler<UpdateAgentSettingsCommand,AgentSettingsResult>>();services.RemoveAll<IRequestHandler<GetKnowledgeQuery,Portfolio.Application.Common.Models.PagedResult<KnowledgeListItem>>>();services.RemoveAll<IRequestHandler<GetKnowledgeDocumentQuery,KnowledgeDetail>>();services.RemoveAll<IRequestHandler<ReindexKnowledgeCommand,string>>();services.RemoveAll<IRequestHandler<ReindexAllKnowledgeCommand,string>>();services.RemoveAll<IRequestHandler<GetConversationsQuery,Portfolio.Application.Common.Models.PagedResult<ConversationListItem>>>();services.RemoveAll<IRequestHandler<GetConversationQuery,ConversationDetail>>();services.RemoveAll<IRequestHandler<CloseConversationCommand,bool>>();
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
            services.AddScoped<IRequestHandler<GetMediaQuery, Portfolio.Application.Common.Models.PagedResult<MediaAssetResult>>, FakeGetMediaHandler>();
            services.AddScoped<IRequestHandler<UploadMediaCommand, MediaAssetResult>, FakeUploadMediaHandler>();
            services.AddScoped<IRequestHandler<UpdateMediaCommand, MediaAssetResult>, FakeUpdateMediaHandler>();
            services.AddScoped<IRequestHandler<DeleteMediaCommand, bool>, FakeDeleteMediaHandler>();
            services.AddScoped<IRequestHandler<CreateChatSessionCommand,ChatSessionResult>,FakeCreateChat>();services.AddScoped<IRequestHandler<SendChatMessageCommand,ChatAnswerResult>,FakeSendChat>();services.AddScoped<IRequestHandler<SubmitChatFeedbackCommand,Guid>,FakeFeedback>();
            services.AddScoped<IRequestHandler<GetAgentSettingsQuery,AgentSettingsResult>,FakeGetAgentSettings>();services.AddScoped<IRequestHandler<UpdateAgentSettingsCommand,AgentSettingsResult>,FakeUpdateAgentSettings>();services.AddScoped<IRequestHandler<GetKnowledgeQuery,Portfolio.Application.Common.Models.PagedResult<KnowledgeListItem>>,FakeKnowledgeList>();services.AddScoped<IRequestHandler<GetKnowledgeDocumentQuery,KnowledgeDetail>,FakeKnowledgeDetail>();services.AddScoped<IRequestHandler<ReindexKnowledgeCommand,string>,FakeReindex>();services.AddScoped<IRequestHandler<ReindexAllKnowledgeCommand,string>,FakeReindexAll>();services.AddScoped<IRequestHandler<GetConversationsQuery,Portfolio.Application.Common.Models.PagedResult<ConversationListItem>>,FakeConversationList>();services.AddScoped<IRequestHandler<GetConversationQuery,ConversationDetail>,FakeConversationDetail>();services.AddScoped<IRequestHandler<CloseConversationCommand,bool>,FakeCloseConversation>();
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
    private static MediaAssetResult Media(Guid? id=null,string type="IMAGE",string? alt="Alt") => new(id??Guid.Parse("99999999-9999-9999-9999-999999999999"),"asset.png","image/png",12,type,"portfolio/2026/08/asset.png","https://cdn.example/asset.png",alt,new DateTimeOffset(2026,8,28,0,0,0,TimeSpan.Zero),new DateTimeOffset(2026,8,28,0,0,0,TimeSpan.Zero));
    public sealed class FakeGetMediaHandler:IRequestHandler<GetMediaQuery,Portfolio.Application.Common.Models.PagedResult<MediaAssetResult>> { public Task<Portfolio.Application.Common.Models.PagedResult<MediaAssetResult>> HandleAsync(GetMediaQuery r,CancellationToken ct=default)=>Task.FromResult(new Portfolio.Application.Common.Models.PagedResult<MediaAssetResult>([Media()],r.Page,r.PageSize,1)); }
    public sealed class FakeUploadMediaHandler:IRequestHandler<UploadMediaCommand,MediaAssetResult> { public Task<MediaAssetResult> HandleAsync(UploadMediaCommand r,CancellationToken ct=default)=>Task.FromResult(Media(type:r.MediaType.Trim().ToUpperInvariant(),alt:r.AltText)); }
    public sealed class FakeUpdateMediaHandler:IRequestHandler<UpdateMediaCommand,MediaAssetResult> { public Task<MediaAssetResult> HandleAsync(UpdateMediaCommand r,CancellationToken ct=default)=>Task.FromResult(Media(r.Id,r.MediaType,r.AltText)); }
    public sealed class FakeDeleteMediaHandler:IRequestHandler<DeleteMediaCommand,bool> { public Task<bool> HandleAsync(DeleteMediaCommand r,CancellationToken ct=default){if(r.Id==Guid.Parse("88888888-8888-8888-8888-888888888888"))throw new Portfolio.Application.Common.Exceptions.ConflictException("MEDIA_IN_USE","The media asset is referenced.");return Task.FromResult(true);} }
    private static readonly Guid ChatSessionId=Guid.Parse("77777777-7777-7777-7777-777777777777");private static readonly Guid ChatMessageId=Guid.Parse("66666666-6666-6666-6666-666666666666");
    public sealed class FakeCreateChat:IRequestHandler<CreateChatSessionCommand,ChatSessionResult>{public Task<ChatSessionResult> HandleAsync(CreateChatSessionCommand r,CancellationToken ct=default)=>Task.FromResult(new ChatSessionResult(ChatSessionId,"ACTIVE","Welcome"));}
    public sealed class FakeSendChat:IRequestHandler<SendChatMessageCommand,ChatAnswerResult>{public Task<ChatAnswerResult> HandleAsync(SendChatMessageCommand r,CancellationToken ct=default){if(r.SessionId!=ChatSessionId)throw new Portfolio.Application.Common.Exceptions.NotFoundException("CHAT_SESSION_NOT_FOUND","Chat session was not found.");if(r.Message=="provider-error")throw new Portfolio.Application.Common.Exceptions.ServiceUnavailableException("AGENT_UNAVAILABLE","The portfolio agent is currently unavailable.");return Task.FromResult(new ChatAnswerResult(ChatMessageId,"Grounded answer",[new("Project","PROJECT",Guid.NewGuid(),"project",1,.9m)]));}}
    public sealed class FakeFeedback:IRequestHandler<SubmitChatFeedbackCommand,Guid>{public Task<Guid> HandleAsync(SubmitChatFeedbackCommand r,CancellationToken ct=default)=>Task.FromResult(Guid.NewGuid());}
    private static AgentSettingsResult Settings(bool enabled=true)=>new(Guid.NewGuid(),"portfolio-agent",enabled,"OpenAI","chat","OpenAI","embed",1536,"Ground answers", "Welcome","Fallback",6,.6m,.2m);
    public sealed class FakeGetAgentSettings:IRequestHandler<GetAgentSettingsQuery,AgentSettingsResult>{public Task<AgentSettingsResult> HandleAsync(GetAgentSettingsQuery r,CancellationToken ct=default)=>Task.FromResult(Settings());}
    public sealed class FakeUpdateAgentSettings:IRequestHandler<UpdateAgentSettingsCommand,AgentSettingsResult>{public Task<AgentSettingsResult> HandleAsync(UpdateAgentSettingsCommand r,CancellationToken ct=default)=>Task.FromResult(Settings(r.Enabled));}
    public sealed class FakeKnowledgeList:IRequestHandler<GetKnowledgeQuery,Portfolio.Application.Common.Models.PagedResult<KnowledgeListItem>>{public Task<Portfolio.Application.Common.Models.PagedResult<KnowledgeListItem>> HandleAsync(GetKnowledgeQuery r,CancellationToken ct=default)=>Task.FromResult(new Portfolio.Application.Common.Models.PagedResult<KnowledgeListItem>([],r.Page,r.PageSize,0));}
    public sealed class FakeKnowledgeDetail:IRequestHandler<GetKnowledgeDocumentQuery,KnowledgeDetail>{public Task<KnowledgeDetail> HandleAsync(GetKnowledgeDocumentQuery r,CancellationToken ct=default)=>Task.FromResult(new KnowledgeDetail(r.Id,"PROJECT",Guid.NewGuid(),"project:test","Test","Content","hash",1,"INDEXED",DateTimeOffset.UtcNow,null,[]));}
    public sealed class FakeReindex:IRequestHandler<ReindexKnowledgeCommand,string>{public Task<string> HandleAsync(ReindexKnowledgeCommand r,CancellationToken ct=default)=>Task.FromResult("PENDING");}public sealed class FakeReindexAll:IRequestHandler<ReindexAllKnowledgeCommand,string>{public Task<string> HandleAsync(ReindexAllKnowledgeCommand r,CancellationToken ct=default)=>Task.FromResult("PENDING");}
    public sealed class FakeConversationList:IRequestHandler<GetConversationsQuery,Portfolio.Application.Common.Models.PagedResult<ConversationListItem>>{public Task<Portfolio.Application.Common.Models.PagedResult<ConversationListItem>> HandleAsync(GetConversationsQuery r,CancellationToken ct=default)=>Task.FromResult(new Portfolio.Application.Common.Models.PagedResult<ConversationListItem>([],r.Page,r.PageSize,0));}
    public sealed class FakeConversationDetail:IRequestHandler<GetConversationQuery,ConversationDetail>{public Task<ConversationDetail> HandleAsync(GetConversationQuery r,CancellationToken ct=default)=>Task.FromResult(new ConversationDetail(r.Id,ChatSessionId,"ACTIVE",DateTimeOffset.UtcNow,null,0,[]));}public sealed class FakeCloseConversation:IRequestHandler<CloseConversationCommand,bool>{public Task<bool> HandleAsync(CloseConversationCommand r,CancellationToken ct=default)=>Task.FromResult(true);}
}
