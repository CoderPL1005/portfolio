using Portfolio.Api.Contracts.Common;
using Portfolio.Api.Authentication;
using Portfolio.Api.Middleware;
using Portfolio.Application;
using Portfolio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AuthenticationResponseMiddleware>();
app.UseAuthorization();

app.MapGet(
    "/",
    () => Results.Ok(ApiResponse<object>.Ok(new { status = "Portfolio API initialized." })));
app.MapControllers();

app.Run();

public partial class Program;
