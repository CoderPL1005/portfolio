using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

// B.4.5 TEST-ONLY contract: real controller/dispatcher/handlers, synthetic data,
// isolated EF InMemory, no new production types or fake review implementations.
public sealed class DiscoveryHumanReviewApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private const string Route = "/api/v1/admin/job-hunting/discovery";

    [Fact]
    public async Task Existing_candidate_exposes_UNREVIEWED_version_zero_without_changing_prediction()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        var detail = await Read(client, $"{Route}/{id}");
        AssertOriginal(detail);
        Assert.True(detail.TryGetProperty("humanReview", out var review),
            "B45: existing candidate must expose humanReview; no review means UNREVIEWED/version 0.");
        Assert.Equal("UNREVIEWED", review.GetProperty("status").GetString());
        Assert.Equal(0, review.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("contentStatus").ValueKind);
        Assert.Equal(JsonValueKind.Null, review.GetProperty("targetStatus").ValueKind);
    }

    [Fact]
    public async Task Authenticated_CONFIRM_uses_original_prediction_and_records_admin_attribution()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        using var response = await client.PostAsJsonAsync($"{Route}/{id}/reviews",
            new { expectedVersion = 0, action = "CONFIRMED", notes = "Đã đọc nội dung và xác nhận hai kết luận gốc." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = await Data(response);
        Assert.Equal("CONFIRMED", review.GetProperty("status").GetString());
        Assert.Equal(1, review.GetProperty("version").GetInt32());
        Assert.Equal("VALID_RECRUITMENT", review.GetProperty("contentStatus").GetString());
        Assert.Equal("NOT_MATCHED", review.GetProperty("targetStatus").GetString());
        Assert.Equal(AuthApiFactory.AdminId, review.GetProperty("reviewedByAdminId").GetGuid());
        Assert.NotEqual(default, review.GetProperty("reviewedAt").GetDateTimeOffset());
        AssertOriginal(await Read(client, $"{Route}/{id}"));
        await AssertNoPromotion(host);
    }

    [Fact]
    public async Task CORRECT_changes_only_human_decision_and_preserves_immutable_observation_on_reimport()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        var before = await Read(client, $"{Route}/{id}");
        using var response = await client.PostAsJsonAsync($"{Route}/{id}/reviews",
            new { expectedVersion = 0, action = "CORRECTED", contentStatus = "NEEDS_REVIEW",
                targetStatus = "NOT_MATCHED", notes = "Cần kiểm tra thêm tính chất tuyển dụng, giữ nguyên đánh giá phù hợp." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = await Data(response);
        Assert.Equal("CORRECTED", review.GetProperty("status").GetString());
        Assert.Equal("NEEDS_REVIEW", review.GetProperty("contentStatus").GetString());
        Assert.Equal("NOT_MATCHED", review.GetProperty("targetStatus").GetString());
        using var repeat = await client.PostAsJsonAsync($"{Route}/import", Bundle());
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var imported = await Data(repeat);
        Assert.Equal(0, imported.GetProperty("inserted").GetInt32());
        Assert.Equal(1, imported.GetProperty("unchanged").GetInt32());
        var after = await Read(client, $"{Route}/{id}");
        Assert.Equal(before.GetProperty("item").GetRawText(), after.GetProperty("item").GetRawText());
        Assert.Equal("CORRECTED", after.GetProperty("humanReview").GetProperty("status").GetString());
        using var scope = host.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().DiscoveryCandidates.SingleAsync();
        Assert.Equal(DiscoveryReviewRules.Hash(Bundle().Query + "\n" + row.Payload), row.PayloadHash);
        await AssertNoPromotion(host);
    }

    [Fact]
    public async Task UNCERTAIN_has_null_labels_and_is_excluded_from_definitive_ground_truth()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        using var response = await client.PostAsJsonAsync($"{Route}/{id}/reviews",
            new { expectedVersion = 0, action = "UNCERTAIN", notes = "Chưa đủ bằng chứng để kết luận." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = await Data(response);
        Assert.Equal("UNCERTAIN", review.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("contentStatus").ValueKind);
        Assert.Equal(JsonValueKind.Null, review.GetProperty("targetStatus").ValueKind);
        var metrics = await Read(client, $"{Route}/quality-metrics");
        Assert.Equal(1, metrics.GetProperty("counts").GetProperty("reviewed").GetInt32());
        Assert.Equal(1, metrics.GetProperty("counts").GetProperty("uncertain").GetInt32());
        AssertNoGroundTruth(metrics);
        AssertOriginal(await Read(client, $"{Route}/{id}"));
    }

    [Fact]
    public async Task History_endpoint_supports_empty_history_then_keeps_both_review_revisions()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        // Independent RED for the read route: not hidden behind a missing POST.
        var empty = await Read(client, $"{Route}/{id}/reviews?page=1&pageSize=20");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        var notes = new[] { "Lần đầu xác nhận.", "Xem lại: chưa chắc chắn." };
        using var first = await client.PostAsJsonAsync($"{Route}/{id}/reviews",
            new { expectedVersion = 0, action = "CONFIRMED", notes = notes[0] });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await client.PostAsJsonAsync($"{Route}/{id}/reviews",
            new { expectedVersion = 1, action = "UNCERTAIN", notes = notes[1] });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var history = await Read(client, $"{Route}/{id}/reviews?page=1&pageSize=20");
        Assert.Equal(2, history.GetProperty("total").GetInt32());
        var rows = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2, 1 }, rows.Select(x => x.GetProperty("version").GetInt32()));
        Assert.Equal(new[] { "UNCERTAIN", "CONFIRMED" }, rows.Select(x => x.GetProperty("status").GetString()));
        Assert.Equal(notes.Reverse(), rows.Select(x => x.GetProperty("notes").GetString()));
        Assert.All(rows, x => Assert.Equal(AuthApiFactory.AdminId, x.GetProperty("reviewedByAdminId").GetGuid()));
    }

    [Fact]
    public async Task Quality_metrics_count_unreviewed_observations_without_inventing_ground_truth()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        await ImportCandidate(client);
        var metrics = await Read(client, $"{Route}/quality-metrics");
        var counts = metrics.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("unreviewed").GetInt32());
        Assert.Equal(0, counts.GetProperty("reviewed").GetInt32());
        AssertNoGroundTruth(metrics);
    }

    [Fact]
    public async Task B44_control_real_import_list_detail_and_reimport_still_work_without_other_domain_mutations()
    {
        using var host = CreateHost();
        using var client = Admin(host);
        var id = await ImportCandidate(client);
        AssertOriginal(await Read(client, $"{Route}/{id}"));
        using var response = await client.PostAsJsonAsync($"{Route}/import", Bundle());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var repeat = await Data(response);
        Assert.Equal(0, repeat.GetProperty("inserted").GetInt32());
        Assert.Equal(1, repeat.GetProperty("unchanged").GetInt32());
        await AssertNoPromotion(host);
    }

    private WebApplicationFactory<Program> CreateHost()
    {
        var name = $"b45-red-{Guid.NewGuid()}";
        return root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            // TestServer is in-process. No real database, worker or outbound HTTP.
            services.RemoveAll<IApplicationDbContext>();
            services.RemoveAll<IHostedService>();
            services.AddScoped<IApplicationDbContext>(_ => new ContentTestDbContext(
                new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name).Options));
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new DenyNetwork()));
        }));
    }

    private static HttpClient Admin(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = host.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    private static async Task<Guid> ImportCandidate(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync($"{Route}/import", Bundle());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Data(response);
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        var list = await Read(client, Route);
        Assert.Equal(1, list.GetProperty("counts").GetProperty("total").GetInt32());
        return Assert.Single(list.GetProperty("results").GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Read(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Data(response);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private static void AssertOriginal(JsonElement detail)
    {
        var expected = Bundle().Items[0];
        var item = detail.GetProperty("item");
        Assert.Equal(expected.RawContent, item.GetProperty("rawContent").GetString());
        Assert.Equal(expected.ContentHash, item.GetProperty("contentHash").GetString());
        Assert.Equal(expected.SourceUrl, item.GetProperty("sourceUrl").GetString());
        Assert.Equal("VALID_RECRUITMENT", item.GetProperty("contentValidity").GetProperty("status").GetString());
        Assert.Equal("NOT_MATCHED", item.GetProperty("targetMatching").GetProperty("status").GetString());
        Assert.Equal("NOT_CHECKED", item.GetProperty("verificationStatus").GetString());
        Assert.Contains("POST_ID_URL_MISMATCH", item.GetProperty("sourceWarnings").EnumerateArray().Select(x => x.GetString()));
    }

    private static void AssertNoGroundTruth(JsonElement metrics)
    {
        // Minimal wire-contract assertions proposed for human review with RED evidence.
        foreach (var level in new[] { "observationLevel", "uniqueContentLevel" })
        foreach (var dimension in new[] { "contentValidity", "targetMatching" })
            Assert.Equal(0, metrics.GetProperty(level).GetProperty(dimension).GetProperty("eligibleSamples").GetInt32());
    }

    private static async Task AssertNoPromotion(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Empty(await db.RawJobPostings.ToListAsync());
        Assert.Empty(await db.JobPostings.ToListAsync());
        Assert.Empty(await db.JobApplications.ToListAsync());
        Assert.Empty(await db.JobApplicationEvents.ToListAsync());
        Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        Assert.Empty(await db.SubmissionAttemptEvents.ToListAsync());
    }

    private static DiscoveryReviewImport Bundle()
    {
        const string text = "Hiring Senior Java Developer in Hanoi";
        return new(1, "SOCIALAPIS", "synthetic-b45-red", "Backend Hanoi", 1,
        [new(0, "125", "https://www.facebook.com/fixture/posts/pfbidSyntheticB45", text,
            DiscoveryReviewRules.Hash(text), DiscoveryReviewRules.Hash(text), null,
            new("VALID_RECRUITMENT", ["EMPLOYER_HIRING"], "Có nội dung tuyển dụng.", [text]),
            new("NOT_MATCHED", ["SENIORITY_MISMATCH"], "Không phù hợp mục tiêu.", []),
            "NOT_CHECKED", JsonSerializer.SerializeToElement(new { company = (string?)null }),
            ["COMPANY", "PUBLISHED_AT"], ["POST_ID_URL_MISMATCH"], "content-first-v1",
            DateTimeOffset.Parse("2026-10-26T03:00:00Z"))]);
    }

    private sealed class DenyNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("B45_TEST_OUTBOUND_NETWORK_FORBIDDEN");
    }
}
