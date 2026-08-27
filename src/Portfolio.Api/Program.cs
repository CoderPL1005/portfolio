using Portfolio.Api.Contracts.Common;
using Portfolio.Api.Authentication;
using Portfolio.Api.Middleware;
using Portfolio.Application;
using Portfolio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "frontend";
var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
var allowedOrigins = configuredOrigins
    .Concat(builder.Environment.IsDevelopment() ? ["http://localhost:4200"] : [])
    .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var details = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => string.IsNullOrWhiteSpace(entry.Key) ? "request" : entry.Key,
                    entry => entry.Value!.Errors
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The request value is invalid."
                            : error.ErrorMessage)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                ApiResponse.Failure(new ApiError(
                    "VALIDATION_ERROR",
                    "One or more validation errors occurred.",
                    details)));
        };
    });
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<AuthenticationResponseMiddleware>();
app.UseAuthorization();

app.MapGet(
    "/",
    () => Results.Ok(ApiResponse<object>.Ok(new { status = "Portfolio API initialized." })));
app.MapGet(
    "/health",
    () => Results.Ok(ApiResponse<object>.Ok(new { status = "Healthy" })))
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
