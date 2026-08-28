using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.Api;

public sealed class SwaggerTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Development_exposes_swagger_with_bearer_auth_and_multipart_media_upload()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment(Environments.Development));
        using var client = developmentFactory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Swagger generation returned {(int)response.StatusCode}: {responseBody}");
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        Assert.Equal("Portfolio API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _));
        var upload = root.GetProperty("paths")
            .GetProperty("/api/v1/admin/media")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema")
            .GetProperty("properties");
        Assert.Equal("string", upload.GetProperty("file").GetProperty("type").GetString());
        Assert.Equal("binary", upload.GetProperty("file").GetProperty("format").GetString());
        Assert.True(upload.TryGetProperty("mediaType", out _));
        Assert.True(upload.TryGetProperty("altText", out _));
        var bearer = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task Production_does_not_expose_swagger_middleware()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment(Environments.Production));
        using var client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}
