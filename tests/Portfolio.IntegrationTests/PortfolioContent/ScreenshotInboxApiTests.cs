using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
}
