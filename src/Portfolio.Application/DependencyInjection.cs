using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Behaviors;
using Portfolio.Application.Common.Messaging;

namespace Portfolio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRequestDispatcher, RequestDispatcher>();

        // Registration order defines the pipeline: validation -> logging -> handler.
        services.AddTransient(typeof(IRequestBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IRequestBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
