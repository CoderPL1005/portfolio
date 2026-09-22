using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class CanonicalCvApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private static readonly byte[] Pdf = "%PDF-1.7\nprivate-cv"u8.ToArray();

    [Fact]
    public async Task Every_canonical_cv_route_requires_admin_authentication()
    {
        using var client = root.CreateClient();
        using var form = UploadForm(Pdf, "CV.pdf", 0);

        using var metadata = await client.GetAsync("/api/v1/admin/job-hunting/canonical-cv");
        using var content = await client.GetAsync("/api/v1/admin/job-hunting/canonical-cv/content");
        using var upload = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", form);

        Assert.Equal(HttpStatusCode.Unauthorized, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, content.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    [Fact]
    public async Task Missing_first_upload_replacement_and_stale_conflict_use_safe_contracts()
    {
        var probe = new Probe();
        using var factory = CreateFactory(probe);
        using var client = Authenticated(factory);

        using var missing = await client.GetAsync("/api/v1/admin/job-hunting/canonical-cv");
        var missingText = await missing.Content.ReadAsStringAsync();
        var missingData = JsonSerializer.Deserialize<JsonElement>(missingText).GetProperty("data");
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.False(missingData.GetProperty("isConfigured").GetBoolean());
        Assert.Equal(0, missingData.GetProperty("version").GetInt32());
        Assert.DoesNotContain("storageKey", missingText, StringComparison.OrdinalIgnoreCase);

        using var firstForm = UploadForm(Pdf, "folder/Canonical CV.pdf", 0);
        using var first = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", firstForm);
        var firstText = await first.Content.ReadAsStringAsync();
        var firstData = JsonSerializer.Deserialize<JsonElement>(firstText).GetProperty("data");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, firstData.GetProperty("version").GetInt32());
        Assert.Equal("Canonical CV.pdf", firstData.GetProperty("fileName").GetString());
        Assert.DoesNotContain("storageKey", firstText, StringComparison.OrdinalIgnoreCase);

        using var replacementForm = UploadForm("%PDF-1.7\nreplacement"u8.ToArray(), "New.pdf", 1);
        using var replacement = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", replacementForm);
        var replacementData = (await replacement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        Assert.Equal(2, replacementData.GetProperty("version").GetInt32());
        Assert.Single(probe.Objects);

        using var staleForm = UploadForm(Pdf, "Stale.pdf", 1);
        using var stale = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", staleForm);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CANONICAL_CV_VERSION_CONFLICT", staleBody.GetProperty("error").GetProperty("code").GetString());
        Assert.Single(probe.Objects);
    }

    [Fact]
    public async Task Invalid_and_oversized_pdf_uploads_are_rejected_without_storage_writes()
    {
        var probe = new Probe();
        using var factory = CreateFactory(probe);
        using var client = Authenticated(factory);

        using var invalidForm = UploadForm("not-pdf"u8.ToArray(), "fake.pdf", 0);
        using var invalid = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", invalidForm);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var oversizedBytes = new byte[10 * 1024 * 1024 + 1];
        "%PDF-"u8.CopyTo(oversizedBytes);
        using var oversizedForm = UploadForm(oversizedBytes, "large.pdf", 0);
        using var oversized = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", oversizedForm);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Empty(probe.Objects);
    }

    [Fact]
    public async Task Authenticated_content_download_streams_pdf_with_safe_headers_and_no_storage_key()
    {
        var probe = new Probe();
        using var factory = CreateFactory(probe);
        using var client = Authenticated(factory);
        using var form = UploadForm(Pdf, "My CV.pdf", 0);
        using var upload = await client.PutAsync("/api/v1/admin/job-hunting/canonical-cv", form);
        upload.EnsureSuccessStatusCode();

        using var response = await client.GetAsync("/api/v1/admin/job-hunting/canonical-cv/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("My CV.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain("canonical-cv/", response.Headers.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> CreateFactory(Probe probe)
    {
        return root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRequestHandler<GetCanonicalCvQuery, CanonicalCvResult>>();
            services.RemoveAll<IRequestHandler<GetCanonicalCvContentQuery, CanonicalCvContentResult>>();
            services.RemoveAll<IRequestHandler<UploadCanonicalCvCommand, CanonicalCvResult>>();
            services.AddSingleton(probe);
            services.AddScoped<IRequestHandler<GetCanonicalCvQuery, CanonicalCvResult>>(_ => probe);
            services.AddScoped<IRequestHandler<GetCanonicalCvContentQuery, CanonicalCvContentResult>>(_ => probe);
            services.AddScoped<IRequestHandler<UploadCanonicalCvCommand, CanonicalCvResult>>(_ => probe);
        }));
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

    private static MultipartFormDataContent UploadForm(byte[] bytes, string fileName, int expectedVersion)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(expectedVersion.ToString()), "expectedVersion");
        return form;
    }

    private sealed class Probe :
        IRequestHandler<GetCanonicalCvQuery, CanonicalCvResult>,
        IRequestHandler<GetCanonicalCvContentQuery, CanonicalCvContentResult>,
        IRequestHandler<UploadCanonicalCvCommand, CanonicalCvResult>
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        private CanonicalCvResult? current;
        private string? key;

        public Task<CanonicalCvResult> HandleAsync(GetCanonicalCvQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(current ?? new CanonicalCvResult(false, null, null, null, null, null, 0, null, null));

        public Task<CanonicalCvContentResult> HandleAsync(GetCanonicalCvContentQuery request, CancellationToken cancellationToken = default)
        {
            if (current is null || key is null)
                throw new NotFoundException("CANONICAL_CV_NOT_FOUND", "The canonical CV has not been uploaded.");
            return Task.FromResult(new CanonicalCvContentResult(Objects[key], current.FileName!, "application/pdf"));
        }

        public async Task<CanonicalCvResult> HandleAsync(UploadCanonicalCvCommand request, CancellationToken cancellationToken = default)
        {
            if (request.Content is null || request.FileSize <= 0 || request.FileSize > 10L * 1024 * 1024)
                throw Invalid("A PDF file within the 10 MiB limit is required.");
            using var target = new MemoryStream();
            await request.Content.CopyToAsync(target, cancellationToken);
            var bytes = target.ToArray();
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                throw Invalid("The uploaded file does not contain a valid PDF signature.");
            if (request.ExpectedVersion != (current?.Version ?? 0))
                throw new ConflictException("CANONICAL_CV_VERSION_CONFLICT", "Refresh and try again.");
            var oldKey = key;
            key = $"canonical-cv/{Guid.NewGuid():N}.pdf";
            Objects.Add(key, bytes);
            if (oldKey is not null) Objects.Remove(oldKey);
            var now = DateTimeOffset.UtcNow;
            var leaf = (request.FileName ?? "CV.pdf").Replace('\\', '/').Split('/').Last();
            current = new CanonicalCvResult(
                true, current?.Id ?? Guid.NewGuid(), leaf, "application/pdf", bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                (current?.Version ?? 0) + 1, current?.CreatedAt ?? now, now);
            return current;
        }

        private static ValidationException Invalid(string message) =>
            new([new ValidationFailure("file", message)]);
    }
}
