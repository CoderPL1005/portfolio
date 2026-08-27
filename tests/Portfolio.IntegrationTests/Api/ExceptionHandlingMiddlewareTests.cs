using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Portfolio.Api.Middleware;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.IntegrationTests.Api;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Theory]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS")]
    [InlineData("forbidden", StatusCodes.Status403Forbidden, "ADMIN_DISABLED")]
    [InlineData("not-found", StatusCodes.Status404NotFound, "PROJECT_NOT_FOUND")]
    [InlineData("conflict", StatusCodes.Status409Conflict, "PROJECT_SLUG_EXISTS")]
    public async Task Known_exception_maps_to_contract_response(
        string exceptionKind,
        int expectedStatus,
        string expectedCode)
    {
        var exception = CreateKnownException(exceptionKind, expectedCode);

        var context = await ExecuteAsync(exception);
        using var response = await ReadResponseAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCode, response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_exception_returns_details_and_bad_request()
    {
        var exception = new ValidationException(
            [new ValidationFailure("title", "Title is required.")]);

        var context = await ExecuteAsync(exception);
        using var response = await ReadResponseAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var error = response.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal(
            "Title is required.",
            error.GetProperty("details").GetProperty("title")[0].GetString());
    }

    [Fact]
    public async Task Unexpected_exception_returns_safe_internal_error()
    {
        const string sensitiveMessage = "sensitive internal provider detail";

        var context = await ExecuteAsync(new InvalidOperationException(sensitiveMessage));
        using var response = await ReadResponseAsync(context);
        var body = response.RootElement.GetRawText();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(
            "INTERNAL_ERROR",
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(sensitiveMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception CreateKnownException(string kind, string code) => kind switch
    {
        "unauthorized" => new UnauthorizedException(code, "Authentication failed."),
        "forbidden" => new ForbiddenException(code, "The operation is forbidden."),
        "not-found" => new NotFoundException(code, "The resource was not found."),
        "conflict" => new ConflictException(code, "The resource conflicts with existing state."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static async Task<DefaultHttpContext> ExecuteAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            new TestLogger<ExceptionHandlingMiddleware>());

        await middleware.InvokeAsync(context);
        return context;
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}

internal sealed class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
