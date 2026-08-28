using Microsoft.OpenApi.Models;
using Portfolio.Api.Contracts.Common;
using Portfolio.Api.Authentication;
using Portfolio.Api.Middleware;
using Portfolio.Api.Startup;
using Portfolio.Application;
using Portfolio.Infrastructure;
using Portfolio.Infrastructure.Persistence.Seeding;

var seedRequested = DatabaseSeedMode.IsRequested(args);
var builder = WebApplication.CreateBuilder(DatabaseSeedMode.WithoutSeedArgument(args));

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Portfolio API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter the raw JWT access token. The Bearer prefix is added automatically.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = Array.Empty<string>()
    });
});
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Portfolio API v1"));
}

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

if (seedRequested)
{
    var seedPath = DatabaseSeedMode.ResolveSeedPath(
        builder.Environment.ContentRootPath,
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory());
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync(seedPath);
    return;
}

app.Run();

public partial class Program;
