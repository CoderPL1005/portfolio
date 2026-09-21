using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Infrastructure.Authentication;
using Portfolio.Infrastructure.Persistence;
using Portfolio.Infrastructure.Persistence.Seeding;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Infrastructure.Storage;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Common.Abstractions.Chat;
using Portfolio.Infrastructure.AI;
using Portfolio.Infrastructure.ChatProtection;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Configuration;
using Portfolio.Infrastructure.Integrations.Telegram;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Infrastructure.Notifications;
using System.Net.Mail;

namespace Portfolio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Database"),
                npgsql => npgsql.UseVector()));
        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddOptions<R2Settings>().Bind(configuration.GetSection(R2Settings.SectionName));
        services.AddSingleton<IFileStorage, R2FileStorage>();
        services.AddOptions<GeminiSettings>().Bind(configuration.GetSection(GeminiSettings.SectionName));
        services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<IRetrievalQueryRewriter, GeminiRetrievalQueryRewriter>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<IChatCompletionService, GeminiChatCompletionService>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<IJobAnalysisService, GeminiJobAnalysisService>(client =>
            client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<IKnowledgeRetriever, PgvectorKnowledgeRetriever>();
        services.AddScoped<IKnowledgeIndexer, KnowledgeIndexer>();
        services.AddHostedService<KnowledgeIndexingWorker>();
        services.AddOptions<ChatProtectionSettings>()
            .Bind(configuration.GetSection(ChatProtectionSettings.SectionName))
            .Validate(settings => settings.BurstPermitLimit > 0, "ChatProtection:BurstPermitLimit must be positive.")
            .Validate(settings => settings.BurstWindowSeconds > 0, "ChatProtection:BurstWindowSeconds must be positive.")
            .Validate(settings => settings.DailyPerVisitorLimit > 0, "ChatProtection:DailyPerVisitorLimit must be positive.")
            .Validate(settings => settings.SessionUserMessageLimit > 0, "ChatProtection:SessionUserMessageLimit must be positive.")
            .Validate(settings => settings.GlobalDailyLimit >= settings.DailyPerVisitorLimit, "ChatProtection:GlobalDailyLimit must be at least DailyPerVisitorLimit.")
            .Validate(settings => settings.IpHashSecret?.Length >= 32, "ChatProtection:IpHashSecret must contain at least 32 characters.")
            .ValidateOnStart();
        services.AddScoped<IChatQuotaService, PostgresChatQuotaService>();
        services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient<ITelegramBotClient, TelegramBotClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers();
        services.AddOptions<WebPushSettings>()
            .Bind(configuration.GetSection(WebPushSettings.SectionName))
            .Validate(settings => IsValidWebPushSubject(settings.Subject),
                "WebPush:Subject must be a valid mailto: or HTTPS URI.")
            .Validate(settings => IsValidWebPushKey(settings.PublicKey),
                "WebPush:PublicKey must be a valid base64url value no longer than 512 characters.")
            .Validate(settings => IsValidWebPushKey(settings.PrivateKey),
                "WebPush:PrivateKey must be a valid base64url value no longer than 512 characters.");
        services.AddSingleton<IWebPushSender, WebPushSender>();
        services.AddSingleton<IIngestionKeyConflictDetector, NpgsqlIngestionKeyConflictDetector>();
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer), "Jwt:Issuer is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "Jwt:Audience is required.")
            .Validate(settings => settings.SecretKey?.Length >= 32, "Jwt:SecretKey must contain at least 32 characters.")
            .Validate(settings => settings.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes must be between 1 and 60.")
            .Validate(settings => settings.RefreshTokenDays is > 0 and <= 30, "Jwt:RefreshTokenDays must be between 1 and 30.")
            .ValidateOnStart();
        services.AddScoped<PortfolioSeeder>();
        services.AddScoped<AdminSeeder>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    private static bool IsValidWebPushSubject(string? subject)
    {
        if (!Uri.TryCreate(subject, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return !string.IsNullOrWhiteSpace(uri.Host);
        }

        return uri.Scheme == Uri.UriSchemeMailto &&
            MailAddress.TryCreate(subject!["mailto:".Length..], out _);
    }

    private static bool IsValidWebPushKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 512 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '=');
}
