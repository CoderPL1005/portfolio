using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Api.ChatProtection;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Chat;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class ChatProtectionApiTests
{
    [Fact]
    public async Task Message_endpoint_limits_fourth_request_per_ip_without_limiting_session_or_feedback()
    {
        await using var factory = CreateBurstFactory(out var probe);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-IP", "203.0.113.10");
        var path = "/api/v1/public/chat/sessions/77777777-7777-7777-7777-777777777777/messages";

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(path, new { message = "Project?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("CHAT_RATE_LIMITED", body);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.DoesNotContain("203.0.113.10", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ip:", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, probe.Calls);

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/v1/public/chat/sessions", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync(
                "/api/v1/public/chat/messages/66666666-6666-6666-6666-666666666666/feedback",
                new { rating = "POSITIVE" })).StatusCode);

        using var otherIp = factory.CreateClient();
        otherIp.DefaultRequestHeaders.Add("X-Test-IP", "203.0.113.11");
        Assert.Equal(HttpStatusCode.OK,
            (await otherIp.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
    }

    [Fact]
    public async Task Burst_bucket_allows_messages_after_fixed_window_resets()
    {
        await using var factory = CreateBurstFactory(out _, 1);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-IP", "198.51.100.20");
        var path = "/api/v1/public/chat/sessions/77777777-7777-7777-7777-777777777777/messages";
        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateBurstFactory(out HandlerProbe probe, int windowSeconds = 60)
    {
        probe = new HandlerProbe();
        var capturedProbe = probe;
        return new AuthApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ChatProtection:BurstPermitLimit"] = "3",
                    ["ChatProtection:BurstWindowSeconds"] = windowSeconds.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientIdentityProvider>();
                services.AddSingleton<IClientIdentityProvider, HeaderTestIdentityProvider>();
                services.RemoveAll<IRequestHandler<SendChatMessageCommand, ChatAnswerResult>>();
                services.AddSingleton(capturedProbe);
                services.AddScoped<IRequestHandler<SendChatMessageCommand, ChatAnswerResult>, ProbedChatHandler>();
            });
        });
    }

    private sealed class HeaderTestIdentityProvider : IClientIdentityProvider
    {
        public string GetNormalizedIp(HttpContext context) =>
            context.Request.Headers["X-Test-IP"].ToString();

        public string GetVisitorKey(HttpContext context) => $"ip:{GetNormalizedIp(context)}";
    }

    public sealed class HandlerProbe
    {
        private int calls;
        public int Calls => calls;
        public void Record() => Interlocked.Increment(ref calls);
    }

    private sealed class ProbedChatHandler(HandlerProbe probe)
        : IRequestHandler<SendChatMessageCommand, ChatAnswerResult>
    {
        public Task<ChatAnswerResult> HandleAsync(
            SendChatMessageCommand request,
            CancellationToken cancellationToken = default)
        {
            probe.Record();
            return Task.FromResult(new ChatAnswerResult(Guid.NewGuid(), "answer", []));
        }
    }
}
