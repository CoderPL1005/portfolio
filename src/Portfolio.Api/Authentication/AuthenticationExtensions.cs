using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Api.Contracts.Common;
using Portfolio.Api.ChatProtection;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Infrastructure.ChatProtection;
using Portfolio.Infrastructure.Authentication;

namespace Portfolio.Api.Authentication;

public static class AuthenticationExtensions
{
    public const string PublicChatMessagePolicy = "public-chat-message";
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddHttpContextAccessor();
        services.AddSingleton<IClientIdentityProvider, ClientIdentityProvider>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, configuredSettings) =>
            {
                var jwt = configuredSettings.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("auth-login", limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
            var protection = configuration.GetSection(ChatProtectionSettings.SectionName)
                .Get<ChatProtectionSettings>() ?? new ChatProtectionSettings();
            options.AddPolicy(PublicChatMessagePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.RequestServices.GetRequiredService<IClientIdentityProvider>().GetNormalizedIp(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = protection.BurstPermitLimit, Window = TimeSpan.FromSeconds(protection.BurstWindowSeconds), QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                var isChatMessage = context.HttpContext.GetEndpoint()?
                    .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == PublicChatMessagePolicy;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Failure(isChatMessage
                        ? new ApiError("CHAT_RATE_LIMITED", "Too many messages. Please wait a moment and try again.")
                        : new ApiError("TOO_MANY_REQUESTS", "Too many requests.")),
                    cancellationToken);
            };
        });

        return services;
    }
}
