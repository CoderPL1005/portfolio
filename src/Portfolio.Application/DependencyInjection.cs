using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Behaviors;
using Portfolio.Application.Common.Messaging;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.Auth.GetCurrentAdmin;
using Portfolio.Application.Features.Auth;
using Portfolio.Application.Features.Auth.Login;
using Portfolio.Application.Features.Auth.Logout;
using Portfolio.Application.Features.Auth.Refresh;
using Portfolio.Application.Features.Certificates;
using Portfolio.Application.Features.Education;
using Portfolio.Application.Features.Experiences;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Application.Features.Profile.GetAdminProfile;
using Portfolio.Application.Features.Profile.UpdateProfile;
using Portfolio.Application.Features.Trainings;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Projects;
using Portfolio.Application.Features.Skills;
using Portfolio.Application.Features.Technologies;
using Portfolio.Application.Features.Dashboard;
using Portfolio.Application.Features.Journey;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.SiteSettings;
using Portfolio.Application.Features.SocialLinks;
using Portfolio.Application.Features.Media;
using Portfolio.Application.Features.Agent;
using Portfolio.Application.Features.Chat;

namespace Portfolio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRequestDispatcher, RequestDispatcher>();

        // Registration order defines the pipeline: validation -> logging -> handler.
        services.AddTransient(typeof(IRequestBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IRequestBehavior<,>), typeof(LoggingBehavior<,>));

        services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, LoginCommandHandler>();
        services.AddScoped<IRequestValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IRequestHandler<RefreshCommand, RefreshResult>, RefreshCommandHandler>();
        services.AddScoped<IRequestValidator<RefreshCommand>, RefreshCommandValidator>();
        services.AddScoped<IRequestHandler<LogoutCommand, bool>, LogoutCommandHandler>();
        services.AddScoped<IRequestValidator<LogoutCommand>, LogoutCommandValidator>();
        services.AddScoped<IRequestHandler<GetCurrentAdminQuery, CurrentAdminResult>, GetCurrentAdminQueryHandler>();
        services.AddScoped<IRequestHandler<GetPublicPortfolioQuery, PortfolioHomeResult>, GetPublicPortfolioQueryHandler>();
        services.AddScoped<IRequestHandler<GetAdminProfileQuery, AdminProfileResult>, GetAdminProfileQueryHandler>();
        services.AddScoped<IRequestHandler<UpdateProfileCommand, AdminProfileResult>, UpdateProfileCommandHandler>();
        services.AddScoped<IRequestValidator<UpdateProfileCommand>, UpdateProfileCommandValidator>();

        services.AddScoped<IRequestHandler<GetExperiencesQuery, IReadOnlyCollection<AdminExperienceResult>>, GetExperiencesQueryHandler>();
        services.AddScoped<IRequestHandler<GetExperienceQuery, AdminExperienceResult>, GetExperienceQueryHandler>();
        services.AddScoped<IRequestHandler<CreateExperienceCommand, AdminExperienceResult>, CreateExperienceCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateExperienceCommand, AdminExperienceResult>, UpdateExperienceCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteExperienceCommand, bool>, DeleteExperienceCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderExperiencesCommand, bool>, ReorderExperiencesCommandHandler>();
        services.AddScoped<IRequestValidator<CreateExperienceCommand>, CreateExperienceCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateExperienceCommand>, UpdateExperienceCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderExperiencesCommand>, ReorderExperiencesCommandValidator>();

        services.AddScoped<IRequestHandler<GetEducationsQuery, IReadOnlyCollection<EducationResult>>, GetEducationsQueryHandler>();
        services.AddScoped<IRequestHandler<GetEducationQuery, EducationResult>, GetEducationQueryHandler>();
        services.AddScoped<IRequestHandler<CreateEducationCommand, EducationResult>, CreateEducationCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateEducationCommand, EducationResult>, UpdateEducationCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteEducationCommand, bool>, DeleteEducationCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderEducationsCommand, bool>, ReorderEducationsCommandHandler>();
        services.AddScoped<IRequestValidator<CreateEducationCommand>, CreateEducationCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateEducationCommand>, UpdateEducationCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderEducationsCommand>, ReorderEducationsCommandValidator>();

        services.AddScoped<IRequestHandler<GetTrainingsQuery, IReadOnlyCollection<TrainingResult>>, GetTrainingsQueryHandler>();
        services.AddScoped<IRequestHandler<GetTrainingQuery, TrainingResult>, GetTrainingQueryHandler>();
        services.AddScoped<IRequestHandler<CreateTrainingCommand, TrainingResult>, CreateTrainingCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateTrainingCommand, TrainingResult>, UpdateTrainingCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteTrainingCommand, bool>, DeleteTrainingCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderTrainingsCommand, bool>, ReorderTrainingsCommandHandler>();
        services.AddScoped<IRequestValidator<CreateTrainingCommand>, CreateTrainingCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateTrainingCommand>, UpdateTrainingCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderTrainingsCommand>, ReorderTrainingsCommandValidator>();

        services.AddScoped<IRequestHandler<GetCertificatesQuery, IReadOnlyCollection<CertificateResult>>, GetCertificatesQueryHandler>();
        services.AddScoped<IRequestHandler<GetCertificateQuery, CertificateResult>, GetCertificateQueryHandler>();
        services.AddScoped<IRequestHandler<CreateCertificateCommand, CertificateResult>, CreateCertificateCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateCertificateCommand, CertificateResult>, UpdateCertificateCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteCertificateCommand, bool>, DeleteCertificateCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderCertificatesCommand, bool>, ReorderCertificatesCommandHandler>();
        services.AddScoped<IRequestValidator<CreateCertificateCommand>, CreateCertificateCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateCertificateCommand>, UpdateCertificateCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderCertificatesCommand>, ReorderCertificatesCommandValidator>();

        services.AddScoped<IRequestHandler<GetTechnologiesQuery, IReadOnlyCollection<TechnologyResult>>, GetTechnologiesQueryHandler>();
        services.AddScoped<IRequestHandler<CreateTechnologyCommand, TechnologyResult>, CreateTechnologyCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateTechnologyCommand, TechnologyResult>, UpdateTechnologyCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteTechnologyCommand, bool>, DeleteTechnologyCommandHandler>();
        services.AddScoped<IRequestValidator<CreateTechnologyCommand>, CreateTechnologyCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateTechnologyCommand>, UpdateTechnologyCommandValidator>();

        services.AddScoped<IRequestHandler<GetSkillsQuery, IReadOnlyCollection<SkillResult>>, GetSkillsQueryHandler>();
        services.AddScoped<IRequestHandler<CreateSkillCommand, SkillResult>, CreateSkillCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateSkillCommand, SkillResult>, UpdateSkillCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteSkillCommand, bool>, DeleteSkillCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderSkillsCommand, bool>, ReorderSkillsCommandHandler>();
        services.AddScoped<IRequestValidator<CreateSkillCommand>, CreateSkillCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateSkillCommand>, UpdateSkillCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderSkillsCommand>, ReorderSkillsCommandValidator>();

        services.AddScoped<IRequestHandler<GetProjectsQuery, PagedResult<ProjectListItemResult>>, GetProjectsQueryHandler>();
        services.AddScoped<IRequestHandler<GetProjectQuery, ProjectResult>, GetProjectQueryHandler>();
        services.AddScoped<IRequestHandler<CreateProjectCommand, ProjectResult>, CreateProjectCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateProjectCommand, ProjectResult>, UpdateProjectCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteProjectCommand, bool>, DeleteProjectCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderProjectsCommand, bool>, ReorderProjectsCommandHandler>();
        services.AddScoped<IRequestHandler<ReplaceProjectTechnologiesCommand, IReadOnlyCollection<ProjectTechnologyResult>>, ReplaceProjectTechnologiesCommandHandler>();
        services.AddScoped<IRequestHandler<GetProjectSectionsQuery, IReadOnlyCollection<ProjectSectionResult>>, GetProjectSectionsQueryHandler>();
        services.AddScoped<IRequestHandler<CreateProjectSectionCommand, ProjectSectionResult>, CreateProjectSectionCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateProjectSectionCommand, ProjectSectionResult>, UpdateProjectSectionCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteProjectSectionCommand, bool>, DeleteProjectSectionCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderProjectSectionsCommand, bool>, ReorderProjectSectionsCommandHandler>();
        services.AddScoped<IRequestHandler<GetProjectMediaQuery, IReadOnlyCollection<ProjectMediaResult>>, GetProjectMediaQueryHandler>();
        services.AddScoped<IRequestHandler<AttachProjectMediaCommand, ProjectMediaResult>, AttachProjectMediaCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateProjectMediaCommand, ProjectMediaResult>, UpdateProjectMediaCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteProjectMediaCommand, bool>, DeleteProjectMediaCommandHandler>();
        services.AddScoped<IRequestHandler<GetPublicProjectsQuery, IReadOnlyCollection<PublicProjectListItem>>, GetPublicProjectsQueryHandler>();
        services.AddScoped<IRequestHandler<GetPublicProjectBySlugQuery, PublicProjectDetail>, GetPublicProjectBySlugQueryHandler>();
        services.AddScoped<IRequestValidator<GetProjectsQuery>, GetProjectsQueryValidator>();
        services.AddScoped<IRequestValidator<CreateProjectCommand>, CreateProjectCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateProjectCommand>, UpdateProjectCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderProjectsCommand>, ReorderProjectsCommandValidator>();
        services.AddScoped<IRequestValidator<ReplaceProjectTechnologiesCommand>, ReplaceProjectTechnologiesCommandValidator>();
        services.AddScoped<IRequestValidator<CreateProjectSectionCommand>, CreateProjectSectionCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateProjectSectionCommand>, UpdateProjectSectionCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderProjectSectionsCommand>, ReorderProjectSectionsCommandValidator>();
        services.AddScoped<IRequestValidator<AttachProjectMediaCommand>, AttachProjectMediaCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateProjectMediaCommand>, UpdateProjectMediaCommandValidator>();

        services.AddScoped<IRequestHandler<GetJourneyItemsQuery, IReadOnlyCollection<JourneyResult>>, GetJourneyItemsQueryHandler>();
        services.AddScoped<IRequestHandler<GetJourneyItemQuery, JourneyResult>, GetJourneyItemQueryHandler>();
        services.AddScoped<IRequestHandler<CreateJourneyItemCommand, JourneyResult>, CreateJourneyItemCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateJourneyItemCommand, JourneyResult>, UpdateJourneyItemCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteJourneyItemCommand, bool>, DeleteJourneyItemCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderJourneyItemsCommand, bool>, ReorderJourneyItemsCommandHandler>();
        services.AddScoped<IRequestValidator<CreateJourneyItemCommand>, CreateJourneyItemCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateJourneyItemCommand>, UpdateJourneyItemCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderJourneyItemsCommand>, ReorderJourneyItemsCommandValidator>();
        services.AddScoped<IRequestHandler<GetSocialLinksQuery, IReadOnlyCollection<SocialLinkResult>>, GetSocialLinksQueryHandler>();
        services.AddScoped<IRequestHandler<CreateSocialLinkCommand, SocialLinkResult>, CreateSocialLinkCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateSocialLinkCommand, SocialLinkResult>, UpdateSocialLinkCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteSocialLinkCommand, bool>, DeleteSocialLinkCommandHandler>();
        services.AddScoped<IRequestHandler<ReorderSocialLinksCommand, bool>, ReorderSocialLinksCommandHandler>();
        services.AddScoped<IRequestValidator<CreateSocialLinkCommand>, CreateSocialLinkCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateSocialLinkCommand>, UpdateSocialLinkCommandValidator>();
        services.AddScoped<IRequestValidator<ReorderSocialLinksCommand>, ReorderSocialLinksCommandValidator>();
        services.AddScoped<IRequestHandler<GetSiteSettingsQuery, SiteSettingsResult>, GetSiteSettingsQueryHandler>();
        services.AddScoped<IRequestHandler<UpdateSiteSettingsCommand, SiteSettingsResult>, UpdateSiteSettingsCommandHandler>();
        services.AddScoped<IRequestValidator<UpdateSiteSettingsCommand>, UpdateSiteSettingsCommandValidator>();
        services.AddScoped<IRequestHandler<GetDashboardQuery, DashboardResult>, GetDashboardQueryHandler>();
        services.AddScoped<IRequestHandler<GetMediaQuery, Common.Models.PagedResult<MediaAssetResult>>, GetMediaQueryHandler>();
        services.AddScoped<IRequestHandler<UploadMediaCommand, MediaAssetResult>, UploadMediaCommandHandler>();
        services.AddScoped<IRequestHandler<UpdateMediaCommand, MediaAssetResult>, UpdateMediaCommandHandler>();
        services.AddScoped<IRequestHandler<DeleteMediaCommand, bool>, DeleteMediaCommandHandler>();
        services.AddScoped<IRequestValidator<GetMediaQuery>, GetMediaQueryValidator>();
        services.AddScoped<IRequestValidator<UploadMediaCommand>, UploadMediaCommandValidator>();
        services.AddScoped<IRequestValidator<UpdateMediaCommand>, UpdateMediaCommandValidator>();
        services.AddScoped<PortfolioKnowledgeBuilder>();
        services.AddScoped<IRequestHandler<GetAgentSettingsQuery,AgentSettingsResult>,GetAgentSettingsQueryHandler>();
        services.AddScoped<IRequestHandler<UpdateAgentSettingsCommand,AgentSettingsResult>,UpdateAgentSettingsCommandHandler>();
        services.AddScoped<IRequestValidator<UpdateAgentSettingsCommand>,UpdateAgentSettingsCommandValidator>();
        services.AddScoped<IRequestHandler<GetKnowledgeQuery,Common.Models.PagedResult<KnowledgeListItem>>,GetKnowledgeQueryHandler>();
        services.AddScoped<IRequestHandler<GetKnowledgeDocumentQuery,KnowledgeDetail>,GetKnowledgeDocumentQueryHandler>();
        services.AddScoped<IRequestHandler<ReindexKnowledgeCommand,string>,ReindexKnowledgeCommandHandler>();
        services.AddScoped<IRequestHandler<ReindexAllKnowledgeCommand,string>,ReindexAllKnowledgeCommandHandler>();
        services.AddScoped<IRequestValidator<GetKnowledgeQuery>,KnowledgeQueryValidator>();
        services.AddScoped<IRequestHandler<CreateChatSessionCommand,ChatSessionResult>,CreateChatSessionCommandHandler>();
        services.AddScoped<IRequestHandler<SendChatMessageCommand,ChatAnswerResult>,SendChatMessageCommandHandler>();
        services.AddScoped<IRequestHandler<SubmitChatFeedbackCommand,Guid>,SubmitChatFeedbackCommandHandler>();
        services.AddScoped<IRequestValidator<SendChatMessageCommand>,SendChatMessageValidator>();
        services.AddScoped<IRequestValidator<SubmitChatFeedbackCommand>,SubmitChatFeedbackValidator>();
        services.AddScoped<IRequestHandler<GetConversationsQuery,Common.Models.PagedResult<ConversationListItem>>,GetConversationsQueryHandler>();
        services.AddScoped<IRequestHandler<GetConversationQuery,ConversationDetail>,GetConversationQueryHandler>();
        services.AddScoped<IRequestHandler<CloseConversationCommand,bool>,CloseConversationCommandHandler>();
        services.AddScoped<IRequestValidator<GetConversationsQuery>,GetConversationsQueryValidator>();

        return services;
    }
}
