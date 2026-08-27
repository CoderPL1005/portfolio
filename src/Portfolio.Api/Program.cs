using Portfolio.Api.Contracts.Common;
using Portfolio.Api.Middleware;
using Portfolio.Application;
using Portfolio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapGet(
    "/",
    () => Results.Ok(ApiResponse<object>.Ok(new { status = "Portfolio API initialized." })));

app.Run();
