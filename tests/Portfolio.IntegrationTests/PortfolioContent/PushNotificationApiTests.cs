using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PushNotifications;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class PushNotificationApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData("POST", "/api/v1/admin/push/subscriptions")]
    [InlineData("DELETE", "/api/v1/admin/push/subscriptions")]
    [InlineData("POST", "/api/v1/admin/push/test")]
    public async Task All_push_routes_require_admin_authentication(string method, string path)
    {
        using var client = root.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new
            {
                endpoint = "https://push.example/subscription",
                p256dh = "public-key",
                auth = "auth-key",
            }),
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorized_routes_use_api_envelope_and_never_return_subscription_secrets()
    {
        using var factory = CreateFactory();
        using var client = Authenticated(factory);
        const string endpoint = "https://push.example/sensitive-endpoint";
        const string p256dh = "sensitive-public-key";
        const string auth = "sensitive-auth-key";

        using var register = await client.PostAsJsonAsync("/api/v1/admin/push/subscriptions", new
        {
            endpoint, p256dh, auth,
        });
        using var disableRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/admin/push/subscriptions")
        {
            Content = JsonContent.Create(new { endpoint }),
        };
        using var disable = await client.SendAsync(disableRequest);
        using var test = await client.PostAsJsonAsync("/api/v1/admin/push/test", new { });

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var combined = string.Join('\n',
            await register.Content.ReadAsStringAsync(),
            await disable.Content.ReadAsStringAsync(),
            await test.Content.ReadAsStringAsync());
        Assert.DoesNotContain(endpoint, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(p256dh, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(auth, combined, StringComparison.Ordinal);
        var testBody = await test.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(testBody.GetProperty("success").GetBoolean());
        Assert.Equal(2, testBody.GetProperty("data").GetProperty("attempted").GetInt32());
        Assert.Equal(1, testBody.GetProperty("data").GetProperty("succeeded").GetInt32());
        Assert.Equal(1, testBody.GetProperty("data").GetProperty("deactivated").GetInt32());
        Assert.Equal(0, testBody.GetProperty("data").GetProperty("failed").GetInt32());
    }

    private WebApplicationFactory<Program> CreateFactory() => root.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            Replace<RegisterPushSubscriptionCommand, bool, FakeRegisterHandler>(services);
            Replace<DisablePushSubscriptionCommand, bool, FakeDisableHandler>(services);
            Replace<SendTestPushNotificationCommand, PushTestSummary, FakeTestHandler>(services);
        }));

    private static void Replace<TRequest, TResult, THandler>(IServiceCollection services)
        where TRequest : IRequest<TResult>
        where THandler : class, IRequestHandler<TRequest, TResult>
    {
        services.RemoveAll<IRequestHandler<TRequest, TResult>>();
        services.AddScoped<IRequestHandler<TRequest, TResult>, THandler>();
    }

    private static HttpClient Authenticated(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    public sealed class FakeRegisterHandler : IRequestHandler<RegisterPushSubscriptionCommand, bool>
    {
        public Task<bool> HandleAsync(RegisterPushSubscriptionCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    public sealed class FakeDisableHandler : IRequestHandler<DisablePushSubscriptionCommand, bool>
    {
        public Task<bool> HandleAsync(DisablePushSubscriptionCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    public sealed class FakeTestHandler : IRequestHandler<SendTestPushNotificationCommand, PushTestSummary>
    {
        public Task<PushTestSummary> HandleAsync(SendTestPushNotificationCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PushTestSummary(2, 1, 1, 0));
    }
}
