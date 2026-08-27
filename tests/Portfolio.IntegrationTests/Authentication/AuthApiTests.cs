using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Auth;
using Portfolio.Application.Features.Auth.Login;

namespace Portfolio.IntegrationTests.Authentication;

public sealed class AuthApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Login_validation_uses_standard_error_envelope()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "invalid-email",
            password = new string('x', 257)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_without_cookie_uses_validation_envelope()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_sets_secure_http_only_cookie_without_serializing_refresh_token()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin@example.com",
            password = "configured-password"
        });
        var json = await response.Content.ReadAsStringAsync();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("portfolio_refresh_token=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-refresh-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Me_rejects_anonymous_request_with_standard_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("UNAUTHORIZED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_returns_contract_for_valid_bearer_without_sensitive_fields()
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var accessToken = tokenService.CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com").Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/v1/auth/me");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(json);
        Assert.Equal(AuthApiFactory.AdminId, body.RootElement.GetProperty("data").GetProperty("id").GetGuid());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unexpected_auth_failure_uses_safe_global_500_envelope()
    {
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<LoginCommand, LoginResult>>();
            services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, ThrowingLoginHandler>();
        }));
        var response = await failingFactory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin@example.com",
            password = "configured-password"
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INTERNAL_ERROR", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("database failure details", body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    public sealed class ThrowingLoginHandler : IRequestHandler<LoginCommand, LoginResult>
    {
        public Task<LoginResult> HandleAsync(LoginCommand request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database failure details");
    }
}
