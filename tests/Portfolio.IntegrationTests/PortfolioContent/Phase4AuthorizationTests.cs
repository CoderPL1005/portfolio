using System.Net;
using System.Text.Json;
using Portfolio.Api.Controllers;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class Phase4AuthorizationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Public_portfolio_is_anonymous_and_uses_contract_envelope()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/public/portfolio");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(json);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Owner", body.RootElement.GetProperty("data").GetProperty("profile").GetProperty("fullName").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("featuredProjects").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("skills").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("journey").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("socialLinks").GetArrayLength());
        Assert.DoesNotContain("singletonKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isPublished", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Every_phase_4a_admin_route_rejects_anonymous_callers(string method, string route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Admin_request_dtos_expose_only_contract_writable_fields()
    {
        AssertProperties<ProfileRequest>("FullName", "ProfessionalTitle", "SecondaryTitle",
            "HeroHeadline", "HeroSummary", "AboutMarkdown", "Email", "Phone", "Location",
            "University", "Major", "AvailabilityStatus", "ProfileImageId", "CvMediaId", "IsPublished");
        AssertProperties<ExperienceRequest>("CompanyName", "RoleTitle", "Location", "StartDate",
            "EndDate", "IsCurrent", "Summary", "ResponsibilitiesMarkdown", "CompanyUrl",
            "DisplayOrder", "IsPublished", "TechnologyIds");
        AssertProperties<EducationRequest>("Institution", "Degree", "FieldOfStudy", "StartDate",
            "EndDate", "Description", "Location", "DisplayOrder", "IsPublished");
        AssertProperties<TrainingRequest>("Title", "Provider", "Description", "StartDate", "EndDate",
            "CredentialUrl", "DisplayOrder", "IsPublished");
        AssertProperties<CertificateRequest>("Name", "Issuer", "IssuedAt", "ExpiresAt", "CredentialId",
            "CredentialUrl", "CertificateMediaId", "DisplayOrder", "IsPublished");
        AssertProperties<ReorderRequest>("Items");
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/v1/admin/profile" },
        { "PUT", "/api/v1/admin/profile" },
        { "GET", "/api/v1/admin/experiences" },
        { "GET", "/api/v1/admin/experiences/11111111-1111-1111-1111-111111111111" },
        { "POST", "/api/v1/admin/experiences" },
        { "PUT", "/api/v1/admin/experiences/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/api/v1/admin/experiences/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/v1/admin/experiences/reorder" },
        { "GET", "/api/v1/admin/education" },
        { "GET", "/api/v1/admin/education/11111111-1111-1111-1111-111111111111" },
        { "POST", "/api/v1/admin/education" },
        { "PUT", "/api/v1/admin/education/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/api/v1/admin/education/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/v1/admin/education/reorder" },
        { "GET", "/api/v1/admin/trainings" },
        { "GET", "/api/v1/admin/trainings/11111111-1111-1111-1111-111111111111" },
        { "POST", "/api/v1/admin/trainings" },
        { "PUT", "/api/v1/admin/trainings/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/api/v1/admin/trainings/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/v1/admin/trainings/reorder" },
        { "GET", "/api/v1/admin/certificates" },
        { "GET", "/api/v1/admin/certificates/11111111-1111-1111-1111-111111111111" },
        { "POST", "/api/v1/admin/certificates" },
        { "PUT", "/api/v1/admin/certificates/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/api/v1/admin/certificates/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/v1/admin/certificates/reorder" }
    };

    private static void AssertProperties<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(expected.Order().ToArray(), actual);
    }
}
