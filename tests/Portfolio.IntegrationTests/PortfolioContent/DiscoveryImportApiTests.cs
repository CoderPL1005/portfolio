using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryImportApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private const string SyntheticKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Theory]
    [InlineData(null, SyntheticKey, true)]
    [InlineData("false", SyntheticKey, true)]
    [InlineData("true", "wrong", true)]
    [InlineData("true", null, true)]
    [InlineData("true", SyntheticKey, false)]
    public async Task Disabled_missing_invalid_or_insecure_machine_credentials_fail_closed(string? enabled, string? supplied, bool https)
    {
        using var factory = Factory(enabled);using var client = factory.CreateClient(new(){BaseAddress=new Uri(https?"https://localhost":"http://localhost")});
        if(supplied is not null)client.DefaultRequestHeaders.Add("X-Discovery-Key",supplied);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/discovery-sync/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/v1/discovery-sync/import",Bundle())).StatusCode);
    }
    [Fact]
    public async Task Scoped_identity_imports_idempotently_but_cannot_read_admin_or_review_and_never_promotes_jobs()
    {
        using var factory=Factory("true");using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        client.DefaultRequestHeaders.Add("X-Discovery-Key",SyntheticKey);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/v1/discovery-sync/ready")).StatusCode);
        var bundle=Bundle();
        var first=await client.PostAsJsonAsync("/api/v1/discovery-sync/import",bundle);Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        var second=await client.PostAsJsonAsync("/api/v1/discovery-sync/import",bundle);Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        var json=await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(0,json.GetProperty("data").GetProperty("inserted").GetInt32());
        Assert.Equal(1,json.GetProperty("data").GetProperty("unchanged").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/admin/job-hunting/discovery")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/v1/admin/job-hunting/discovery/import",bundle)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync($"/api/v1/admin/job-hunting/discovery/{Guid.NewGuid()}/reviews",new{})).StatusCode);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Single(await db.DiscoveryCandidates.ToListAsync());Assert.Empty(await db.DiscoveryCandidateReviews.ToListAsync());
        Assert.Empty(await db.DiscoveryCandidateReviewEvents.ToListAsync());Assert.Empty(await db.JobPostings.ToListAsync());
        Assert.Empty(await db.RawJobPostings.ToListAsync());Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        var invalid=bundle with {Items=[bundle.Items[0] with {VerificationStatus="VERIFIED"}]};
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/discovery-sync/import",invalid)).StatusCode);
    }
    [Fact]
    public async Task Admin_bearer_does_not_substitute_for_machine_credential_and_reimport_preserves_human_history()
    {
        using var factory=Factory("true");using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        using var scope=factory.Services.CreateScope();
        var token=scope.ServiceProvider.GetRequiredService<Portfolio.Application.Common.Abstractions.Authentication.IJwtTokenService>()
            .CreateAccessToken(AuthApiFactory.AdminId,"admin@example.com");
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token.Value);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/discovery-sync/ready")).StatusCode);
        var bundle=Bundle();Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/admin/job-hunting/discovery/import",bundle)).StatusCode);
        var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();var candidate=await db.DiscoveryCandidates.SingleAsync();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/v1/admin/job-hunting/discovery/{candidate.Id}/reviews",
            new{expectedVersion=0,action="CONFIRMED",notes="Synthetic reviewed evidence"})).StatusCode);
        client.DefaultRequestHeaders.Authorization=null;client.DefaultRequestHeaders.Add("X-Discovery-Key",SyntheticKey);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/discovery-sync/import",bundle)).StatusCode);
        Assert.Equal(1,(await db.DiscoveryCandidateReviews.AsNoTracking().SingleAsync()).Version);
        Assert.Single(await db.DiscoveryCandidateReviewEvents.AsNoTracking().ToListAsync());
    }
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory(string? enabled)
    {
        var database=Guid.NewGuid().ToString();
        return root.WithWebHostBuilder(b=>{
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["DiscoveryImport:Enabled"]=enabled,["DiscoveryImport:Key"]=SyntheticKey}));
            b.ConfigureServices(s=>{s.RemoveAll<IApplicationDbContext>();s.AddScoped<IApplicationDbContext>(_=>
                new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(database).Options));});
        });
    }
    private static DiscoveryReviewImport Bundle()
    {
        const string text="Hiring Senior Java Developer in Hanoi";
        return new(1,"SOCIALAPIS","synthetic-machine-import","Backend Hanoi",1,[new(0,"123",
            "https://www.facebook.com/fixture/posts/pfbidSynthetic",text,DiscoveryReviewRules.Hash(text),DiscoveryReviewRules.Hash(text),null,
            new("VALID_RECRUITMENT",["EMPLOYER_HIRING"],"Synthetic recruitment.",[text]),
            new("NOT_MATCHED",["SENIORITY_MISMATCH"],"Synthetic non-target.",[]),"NOT_CHECKED",
            System.Text.Json.JsonSerializer.SerializeToElement(new{company=(string?)null}),["COMPANY"],["POST_ID_URL_MISMATCH"],"content-first-v1",DateTimeOffset.UtcNow)]);
    }
}
