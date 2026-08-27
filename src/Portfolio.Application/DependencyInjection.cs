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

        return services;
    }
}
