using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Infrastructure.Persistence;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryReviewApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData("GET", "/api/v1/admin/job-hunting/discovery")]
    [InlineData("GET", "/api/v1/admin/job-hunting/discovery/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/admin/job-hunting/discovery/import")]
    [InlineData("POST", "/api/v1/admin/job-hunting/discovery/demo-reset-plan")]
    public async Task All_review_routes_require_authentication(string method, string url)
    {
        using var client = root.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact] public async Task Real_handlers_import_list_detail_filters_and_reimport_without_other_mutations()
    {
        var name = Guid.NewGuid().ToString();
        using var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            // Entire test persistence is EF InMemory. No production connection.
            services.RemoveAll<IApplicationDbContext>();
            services.AddScoped<IApplicationDbContext>(_ => new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name).Options));
        }));
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().DiscoveryCandidates.EntityType;
            var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }
        var bundlePath = Environment.GetEnvironmentVariable("DISCOVERY_REVIEW_TEST_BUNDLE");
        var bundle = bundlePath is not null
            ? JsonSerializer.Deserialize<DiscoveryReviewImport>(await File.ReadAllTextAsync(bundlePath), DiscoveryReviewRules.Json)!
            : SyntheticBundle();
        var first = await client.PostAsJsonAsync("/api/v1/admin/job-hunting/discovery/import", bundle);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var again = await client.PostAsJsonAsync("/api/v1/admin/job-hunting/discovery/import", bundle);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeat = (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(0, repeat.GetProperty("inserted").GetInt32()); Assert.Equal(bundle.Items.Length, repeat.GetProperty("unchanged").GetInt32());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/job-hunting/discovery?pageSize=100");
        var data = list.GetProperty("data"); Assert.Equal(bundle.Items.Length, data.GetProperty("counts").GetProperty("total").GetInt32());
        foreach (var row in data.GetProperty("results").GetProperty("items").EnumerateArray())
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/job-hunting/discovery/{row.GetProperty("id").GetGuid()}");
            var item = detail.GetProperty("data").GetProperty("item");
            Assert.Equal("NOT_CHECKED", item.GetProperty("verificationStatus").GetString());
            var expected = bundle.Items[item.GetProperty("itemIndex").GetInt32()];
            Assert.Equal(expected.RawContent, item.GetProperty("rawContent").GetString());
        }
        var invalid = await client.GetAsync("/api/v1/admin/job-hunting/discovery?contentStatus=INVALID");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var unsafeBundle = bundle with { Items = [bundle.Items[0] with { SourceUrl = "https://evil.invalid/" }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/job-hunting/discovery/import", unsafeBundle)).StatusCode);
        using var check = factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Equal(bundle.Items.Length, await db.DiscoveryCandidates.CountAsync());
        Assert.Empty(await db.JobPostings.ToListAsync()); Assert.Empty(await db.RawJobPostings.ToListAsync()); Assert.Empty(await db.SubmissionAttempts.ToListAsync());
    }
    private static DiscoveryReviewImport SyntheticBundle()
    {
        const string text = "Hiring Senior Java Developer";
        return new(1, "SOCIALAPIS", "synthetic-api", "Backend Hanoi", 1,
        [new(0, "125", "https://www.facebook.com/fixture/posts/pfbidExample", text, DiscoveryReviewRules.Hash(text), DiscoveryReviewRules.Hash(text), null,
            new("VALID_RECRUITMENT", ["EMPLOYER_HIRING"], "Có nội dung tuyển dụng.", [text]),
            new("NOT_MATCHED", ["SENIORITY_MISMATCH"], "Không phù hợp.", []), "NOT_CHECKED", JsonSerializer.SerializeToElement(new { company = (string?)null }),
            ["COMPANY"], ["POST_ID_URL_MISMATCH"], "content-first-v1", DateTimeOffset.UtcNow)]);
    }
}
