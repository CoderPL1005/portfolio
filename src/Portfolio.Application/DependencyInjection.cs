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

        return services;
    }
}
