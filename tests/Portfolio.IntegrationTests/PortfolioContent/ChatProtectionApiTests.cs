using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private const string AllowedFrontendOrigin = "http://localhost:4200";

    [Fact]
    public async Task Message_endpoint_limits_fourth_request_per_ip_without_limiting_session_or_feedback()
    {
        await using var factory = CreateBurstFactory(out var probe);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-IP", "203.0.113.10");
        client.DefaultRequestHeaders.Add("Origin", AllowedFrontendOrigin);
        var path = "/api/v1/public/chat/sessions/77777777-7777-7777-7777-777777777777/messages";

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync(path, new { message = "Project?" })).StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(path, new { message = "Project?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(body);
        Assert.Equal("CHAT_RATE_LIMITED",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(AllowedFrontendOrigin,
            Assert.Single(rejected.Headers.GetValues("Access-Control-Allow-Origin")));
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
        otherIp.DefaultRequestHeaders.Add("Origin", "https://unapproved.example");
        var unapprovedOriginResponse = await otherIp.PostAsJsonAsync(path, new { message = "Project?" });
        Assert.Equal(HttpStatusCode.OK, unapprovedOriginResponse.StatusCode);
        Assert.False(unapprovedOriginResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Burst_bucket_allows_messages_after_fixed_window_resets()
    {
        var window = TimeSpan.FromSeconds(1);
        await using var factory = CreateBurstFactory(out var probe, 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        const string path = "/api/v1/public/chat/sessions/77777777-7777-7777-7777-777777777777/messages";

        // Warm up host/middleware/JIT without consuming the measured IP's bucket.
        using (var warmup = factory.CreateClient())
        {
            warmup.DefaultRequestHeaders.Add("X-Test-IP", "198.51.100.19");
            using var response = await warmup.PostAsJsonAsync(path, new { message = "Project?" }, deadline.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // The framework limiter uses its own real timer, not the application's TimeProvider.
        // Only evaluate a burst proven to fit inside its first window. A slow batch is
        // inconclusive, not evidence that the limiter failed; re-observation is bounded.
        for (var batch = 0; batch < 3; batch++)
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-IP", $"198.51.100.{20 + batch}");
            var callsBefore = probe.Calls;
            var started = Stopwatch.GetTimestamp();
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                client.PostAsJsonAsync(path, new { message = "Project?" }, deadline.Token)));
            var elapsed = Stopwatch.GetElapsedTime(started);
            try
            {
                if (elapsed >= window)
                    continue;

                Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
                var rejected = Assert.Single(responses.Where(response =>
                    response.StatusCode == HttpStatusCode.TooManyRequests));
                Assert.Equal(callsBefore + 3, probe.Calls);
                Assert.Equal(window, rejected.Headers.RetryAfter?.Delta);
                using var payload = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(deadline.Token));
                Assert.Equal("CHAT_RATE_LIMITED", payload.RootElement.GetProperty("error").GetProperty("code").GetString());

                var retryAfter = rejected.Headers.RetryAfter!.Delta!.Value;
                // Respect Retry-After; a delayed timer callback may require another
                // header-directed wait, but never an unbounded poll or arbitrary sleep.
                for (var replenishment = 0; replenishment < 3; replenishment++)
                {
                    await Task.Delay(retryAfter, deadline.Token);
                    using var resumed = await client.PostAsJsonAsync(path, new { message = "Project?" }, deadline.Token);
                    if (resumed.StatusCode == HttpStatusCode.OK)
                    {
                        Assert.Equal(callsBefore + 4, probe.Calls);
                        return;
                    }

                    Assert.Equal(HttpStatusCode.TooManyRequests, resumed.StatusCode);
                    Assert.Equal(window, resumed.Headers.RetryAfter?.Delta);
                    Assert.Equal(callsBefore + 3, probe.Calls);
                    retryAfter = resumed.Headers.RetryAfter!.Delta!.Value;
                }

                Assert.Fail("The actual fixed-window limiter did not replenish within three Retry-After intervals.");
            }
            finally
            {
                foreach (var response in responses)
                    response.Dispose();
            }
        }

        Assert.Fail("Could not observe a four-request burst within one fixed window in three bounded attempts.");
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
