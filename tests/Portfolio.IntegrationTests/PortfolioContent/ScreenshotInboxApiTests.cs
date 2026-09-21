using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class ScreenshotInboxApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Screenshot_submission_requires_admin_authentication()
    {
        using var form = Form(Guid.NewGuid(), ("one.png", "image/png"));
        using var response = await root.CreateClient().PostAsync("/api/v1/admin/raw-job-postings/screenshots", form);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_multipart_submission_returns_safe_api_contract()
    {
        var probe = new Probe();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<SubmitJobScreenshotsCommand, ScreenshotSubmissionResult>>();
            services.AddSingleton(probe);
            services.AddScoped<IRequestHandler<SubmitJobScreenshotsCommand, ScreenshotSubmissionResult>>(
                provider => provider.GetRequiredService<Probe>());
        }));
        using var client = Authenticated(factory);
        var submissionId = Guid.NewGuid();
        using var form = Form(submissionId, ("one.png", "image/png"), ("two.jpg", "image/jpeg"));

        using var response = await client.PostAsync("/api/v1/admin/raw-job-postings/screenshots", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<JsonElement>(responseText);
        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.Equal(Probe.RawId, data.GetProperty("rawJobPostingId").GetGuid());
        Assert.Equal("RECEIVED", data.GetProperty("ingestionStatus").GetString());
        Assert.Equal(2, data.GetProperty("attachmentCount").GetInt32());
        Assert.DoesNotContain("storageKey", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(submissionId, probe.SubmissionId);
        Assert.Equal(2, probe.FileCount);
    }

    [Fact]
    public async Task Analyze_requires_authentication()
    {
        using var response = await root.CreateClient().PostAsync($"/api/v1/admin/raw-job-postings/{Guid.NewGuid()}/analyze", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_analyze_uses_only_route_identity_and_returns_job_contract()
    {
        var probe = new AnalyzeProbe();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<AnalyzeRawJobPostingCommand, JobPostingResult>>();
            services.AddSingleton(probe);
            services.AddScoped<IRequestHandler<AnalyzeRawJobPostingCommand, JobPostingResult>>(provider => provider.GetRequiredService<AnalyzeProbe>());
        }));
        using var client = Authenticated(factory);
        var rawId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync($"/api/v1/admin/raw-job-postings/{rawId}/analyze", new
        {
            storageKey = "client-controlled",
            base64 = "private-bytes",
            geminiJson = new { companyName = "spoofed" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(rawId, probe.RawId);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("geminiJson", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Received_list_is_authenticated_and_never_exposes_private_storage_fields()
    {
        using var unauthorized = await root.CreateClient().GetAsync("/api/v1/admin/raw-job-postings?ingestionStatus=RECEIVED");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var probe = new ListProbe();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<GetRawJobPostingsQuery, Portfolio.Application.Common.Models.PagedResult<RawJobPostingListItem>>>();
            services.AddSingleton(probe);
            services.AddScoped<IRequestHandler<GetRawJobPostingsQuery, Portfolio.Application.Common.Models.PagedResult<RawJobPostingListItem>>>(provider => provider.GetRequiredService<ListProbe>());
        }));
        using var client = Authenticated(factory);
        using var response = await client.GetAsync("/api/v1/admin/raw-job-postings?ingestionStatus=RECEIVED");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RECEIVED", probe.Status);
        Assert.DoesNotContain("storageKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attachmentCount", text);
    }

    private static MultipartFormDataContent Form(Guid id, params (string Name, string Type)[] files)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(id.ToString()), "submissionId");
        foreach (var file in files)
        {
            var content = new ByteArrayContent([1]);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.Type);
            form.Add(content, "files", file.Name);
        }
        return form;
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

    private sealed class Probe : IRequestHandler<SubmitJobScreenshotsCommand, ScreenshotSubmissionResult>
    {
        public static readonly Guid RawId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public Guid SubmissionId { get; private set; }
        public int FileCount { get; private set; }
        public Task<ScreenshotSubmissionResult> HandleAsync(SubmitJobScreenshotsCommand request, CancellationToken cancellationToken = default)
        {
            SubmissionId = request.SubmissionId;
            FileCount = request.Files.Count;
            return Task.FromResult(new ScreenshotSubmissionResult(RawId, "RECEIVED", FileCount, true));
        }
    }

    private sealed class AnalyzeProbe : IRequestHandler<AnalyzeRawJobPostingCommand, JobPostingResult>
    {
        public Guid RawId { get; private set; }
        public Task<JobPostingResult> HandleAsync(AnalyzeRawJobPostingCommand request, CancellationToken cancellationToken = default)
        {
            RawId = request.Id;
            return Task.FromResult(JobResult());
        }
    }

    private sealed class ListProbe : IRequestHandler<GetRawJobPostingsQuery, Portfolio.Application.Common.Models.PagedResult<RawJobPostingListItem>>
    {
        public string? Status { get; private set; }
        public Task<Portfolio.Application.Common.Models.PagedResult<RawJobPostingListItem>> HandleAsync(GetRawJobPostingsQuery request, CancellationToken cancellationToken = default)
        {
            Status = request.IngestionStatus;
            var item = new RawJobPostingListItem(Guid.NewGuid(), "MANUAL", "RECEIVED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, 1);
            return Task.FromResult(new Portfolio.Application.Common.Models.PagedResult<RawJobPostingListItem>([item], 1, 20, 1));
        }
    }

    private static JobPostingResult JobResult()
    {
        using var stack = JsonDocument.Parse("[]");
        return new JobPostingResult(
            Guid.NewGuid(), "Company", "Role", "Hanoi", null, null, null, null, null, null, null,
            "Description", stack.RootElement.Clone(), null, null, "PENDING", "PENDING_ANALYSIS", null, null, null, null,
            1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], []);
    }
}
