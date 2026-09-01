using System.Net;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Features.Journey;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.Api;

public sealed class Phase5ApiHardeningTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Health_is_anonymous_safe_and_uses_the_standard_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"success\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Healthy\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Development_cors_allows_the_Angular_origin_with_credentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/portfolio");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task Development_cors_does_not_allow_an_unconfigured_origin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/portfolio");
        request.Headers.Add("Origin", "https://untrusted.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public void Routes_are_unique_and_all_admin_actions_have_authorization_metadata()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => new { Method = method, Route = endpoint.RoutePattern.RawText!, Endpoint = endpoint }))
            .ToList();
        var apiEndpoints = endpoints.Where(x => x.Route.StartsWith("api/", StringComparison.OrdinalIgnoreCase)).ToList();
        var duplicates = apiEndpoints.GroupBy(x => $"{x.Method} {x.Route}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).ToList();
        var adminEndpoints = apiEndpoints.Where(x => x.Route.StartsWith("api/v1/admin/", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Empty(duplicates);
        Assert.Equal(79, adminEndpoints.Count);
        Assert.All(adminEndpoints, endpoint =>
            Assert.NotNull(endpoint.Endpoint.Metadata.GetMetadata<IAuthorizeData>()));
    }

    [Fact]
    public void Public_dtos_expose_only_frozen_contract_fields()
    {
        AssertProperties<PublicProfileResult>("FullName", "ProfessionalTitle", "SecondaryTitle", "HeroHeadline", "HeroSummary", "AboutMarkdown", "Email", "Location", "University", "Major", "AvailabilityStatus", "ProfileImageUrl", "CvUrl");
        AssertProperties<PublicExperienceResult>("Id", "CompanyName", "RoleTitle", "Location", "StartDate", "EndDate", "IsCurrent", "Summary", "ResponsibilitiesMarkdown", "CompanyUrl", "Technologies");
        AssertProperties<PublicEducationResult>("Id", "Institution", "Degree", "FieldOfStudy", "StartDate", "EndDate", "Description", "Location");
        AssertProperties<PublicTrainingResult>("Id", "Title", "Provider", "Description", "StartDate", "EndDate", "CredentialUrl");
        AssertProperties<PublicCertificateResult>("Id", "Name", "Issuer", "IssuedAt", "ExpiresAt", "CredentialId", "CredentialUrl", "CertificateUrl");
        AssertProperties<PublicProjectListItem>("Id", "Slug", "Title", "Subtitle", "ShortDescription", "Role", "Status", "Featured", "ThumbnailUrl", "Technologies");
        AssertProperties<PublicTechnologyResult>("Id", "Name", "Category");
        AssertProperties<PublicSkillResult>("Id", "Name", "Category", "ExperienceLevel", "Description", "TechnologyId");
        AssertProperties<PublicJourneyResult>("Id", "Title", "Subtitle", "Description", "OccurredAt", "IconKey",
            "SourceType", "SourceId", "StartAt", "EndAt", "IsOngoing", "TimelineKind");
        AssertProperties<PublicSocialLinkResult>("Id", "Platform", "Label", "Url", "IconKey");
    }

    [Fact]
    public void Journey_dtos_add_duration_metadata_without_removing_legacy_occurred_at()
    {
        AssertProperties<AdminJourneyTimelineResult>("Id", "Title", "Subtitle", "Description",
            "OccurredAt", "IconKey", "SourceType", "SourceId", "IsManual", "StartAt", "EndAt",
            "IsOngoing", "TimelineKind");
        AssertProperties<PublicJourneyResult>("Id", "Title", "Subtitle", "Description",
            "OccurredAt", "IconKey", "SourceType", "SourceId", "StartAt", "EndAt",
            "IsOngoing", "TimelineKind");
    }

    [Fact]
    public async Task Framework_model_binding_errors_use_the_standard_error_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/public/projects?featured=not-a-boolean");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("error").TryGetProperty("details", out _));
        Assert.False(body.RootElement.TryGetProperty("type", out _));
    }

    private static void AssertProperties<T>(params string[] expected) =>
        Assert.Equal(expected.Order().ToArray(), typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name).Order().ToArray());
}
