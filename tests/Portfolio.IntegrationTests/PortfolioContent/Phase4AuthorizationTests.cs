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
    public async Task Every_implemented_phase_4_admin_route_rejects_anonymous_callers(string method, string route)
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
        AssertProperties<TechnologyRequest>("Name", "Category", "IconKey", "WebsiteUrl", "DisplayOrder", "IsActive");
        AssertProperties<SkillRequest>("Name", "Category", "ExperienceLevel", "Description", "TechnologyId", "DisplayOrder", "IsPublished");
        AssertProperties<ProjectRequest>("Slug", "Title", "Subtitle", "ShortDescription", "OverviewMarkdown", "Role", "TeamSize", "StartDate", "EndDate", "Status", "GithubUrl", "LiveUrl", "ThumbnailMediaId", "Featured", "IsPublished", "DisplayOrder", "SeoTitle", "SeoDescription", "TechnologyIds");
        AssertProperties<ProjectTechnologiesRequest>("Items");
        AssertProperties<ProjectSectionRequest>("SectionType", "Title", "Subtitle", "ContentMarkdown", "Content", "DisplayOrder", "IsVisible");
        AssertProperties<AttachProjectMediaRequest>("MediaAssetId", "MediaRole", "Caption", "DisplayOrder");
        AssertProperties<UpdateProjectMediaRequest>("MediaRole", "Caption", "DisplayOrder");
    }

    [Fact]
    public async Task Public_project_routes_are_anonymous_and_use_contract_envelopes()
    {
        var client = factory.CreateClient(); var list = await client.GetAsync("/api/v1/public/projects?featured=true"); var detail = await client.GetAsync("/api/v1/public/projects/project");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync()); using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("project", listBody.RootElement.GetProperty("data")[0].GetProperty("slug").GetString()); Assert.Equal("project", detailBody.RootElement.GetProperty("data").GetProperty("slug").GetString());
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
        ,{ "GET", "/api/v1/admin/technologies" }
        ,{ "POST", "/api/v1/admin/technologies" }
        ,{ "PUT", "/api/v1/admin/technologies/11111111-1111-1111-1111-111111111111" }
        ,{ "DELETE", "/api/v1/admin/technologies/11111111-1111-1111-1111-111111111111" }
        ,{ "GET", "/api/v1/admin/skills" }
        ,{ "POST", "/api/v1/admin/skills" }
        ,{ "PUT", "/api/v1/admin/skills/11111111-1111-1111-1111-111111111111" }
        ,{ "DELETE", "/api/v1/admin/skills/11111111-1111-1111-1111-111111111111" }
        ,{ "PUT", "/api/v1/admin/skills/reorder" }
        ,{ "GET", "/api/v1/admin/projects" }
        ,{ "GET", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111" }
        ,{ "POST", "/api/v1/admin/projects" }
        ,{ "PUT", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111" }
        ,{ "DELETE", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111" }
        ,{ "PUT", "/api/v1/admin/projects/reorder" }
        ,{ "PUT", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/technologies" }
        ,{ "GET", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/sections" }
        ,{ "POST", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/sections" }
        ,{ "PUT", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/sections/22222222-2222-2222-2222-222222222222" }
        ,{ "DELETE", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/sections/22222222-2222-2222-2222-222222222222" }
        ,{ "PUT", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/sections/reorder" }
        ,{ "GET", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/media" }
        ,{ "POST", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/media" }
        ,{ "PUT", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/media/22222222-2222-2222-2222-222222222222" }
        ,{ "DELETE", "/api/v1/admin/projects/11111111-1111-1111-1111-111111111111/media/22222222-2222-2222-2222-222222222222" }
    };

    private static void AssertProperties<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(expected.Order().ToArray(), actual);
    }
}
