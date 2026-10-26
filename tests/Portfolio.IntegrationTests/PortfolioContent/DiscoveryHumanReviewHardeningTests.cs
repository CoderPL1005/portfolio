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

public sealed class DiscoveryHumanReviewHardeningTests(AuthApiFactory root):IClassFixture<AuthApiFactory>
{
    private const string Route="/api/v1/admin/job-hunting/discovery";
    [Theory]
    [InlineData("POST","/11111111-1111-1111-1111-111111111111/reviews")]
    [InlineData("GET","/11111111-1111-1111-1111-111111111111/reviews")]
    [InlineData("GET","/quality-metrics")]
    public async Task Human_review_routes_require_authentication(string method,string suffix)
    {
        using var client=root.CreateClient();using var response=await client.SendAsync(new HttpRequestMessage(new HttpMethod(method),Route+suffix){Content=JsonContent.Create(new{})});
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
    [Fact] public async Task Real_API_rejects_invalid_inputs_unknown_fields_stale_versions_and_keeps_history()
    {
        using var host=Host();using var client=Client(host);
        using var imported=await client.PostAsJsonAsync(Route+"/import",Bundle());Assert.Equal(HttpStatusCode.OK,imported.StatusCode);
        var list=await Read(client,Route);var id=list.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetGuid();
        var url=$"{Route}/{id}/reviews";
        foreach(var body in new object[]{
            new{action="CONFIRMED",notes="missing version"},
            new{expectedVersion=-1,action="CONFIRMED",notes="negative"},
            new{expectedVersion=0,action="CONFIRMED",notes=" "},
            new{expectedVersion=0,action="CONFIRMED",notes=new string('x',2001)},
            new{expectedVersion=0,action="CONFIRMED",notes="NUL\0"},
            new{expectedVersion=0,action="CONFIRMED",notes="reason",reviewedByAdminId=Guid.NewGuid()},
            new{expectedVersion=0,action="UNCERTAIN",notes="reason",contentStatus="NEEDS_REVIEW"},
            new{expectedVersion=0,action="CORRECTED",notes="reason",contentStatus="VALID_RECRUITMENT",targetStatus="NOT_MATCHED"},
            new{expectedVersion=0,action="CORRECTED",notes="reason",contentStatus="INVALID",targetStatus="NOT_MATCHED"},
            new{expectedVersion=0,action="UNREVIEWED",notes="reason"}})
        {using var invalid=await client.PostAsJsonAsync(url,body);Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);}
        using(var invalidFilter=await client.GetAsync(Route+"?reviewState=INVALID"))Assert.Equal(HttpStatusCode.BadRequest,invalidFilter.StatusCode);
        using(var missing=await client.PostAsJsonAsync($"{Route}/{Guid.NewGuid()}/reviews",new{expectedVersion=0,action="CONFIRMED",notes="reason"}))Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        using(var missingHistory=await client.GetAsync($"{Route}/{Guid.NewGuid()}/reviews"))Assert.Equal(HttpStatusCode.NotFound,missingHistory.StatusCode);
        Assert.Equal(0,(await Read(client,url)).GetProperty("total").GetInt32());
        using(var confirmed=await client.PostAsJsonAsync(url,new{expectedVersion=0,action="CONFIRMED",notes="confirm"}))Assert.Equal(HttpStatusCode.OK,confirmed.StatusCode);
        using(var stale=await client.PostAsJsonAsync(url,new{expectedVersion=0,action="UNCERTAIN",notes="stale"}))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using(var corrected=await client.PostAsJsonAsync(url,new{expectedVersion=1,action="CORRECTED",contentStatus="INVALID_RECRUITMENT",targetStatus="NOT_MATCHED",notes="correction"}))Assert.Equal(HttpStatusCode.OK,corrected.StatusCode);
        var metrics=await Read(client,Route+"/quality-metrics");
        Assert.Equal(1,metrics.GetProperty("counts").GetProperty("corrected").GetInt32());
        Assert.Equal(1,metrics.GetProperty("observationLevel").GetProperty("contentValidity").GetProperty("eligibleSamples").GetInt32());
        Assert.Equal(2,(await Read(client,url)).GetProperty("total").GetInt32());
        Assert.Equal(1,(await Read(client,Route+"?reviewState=CORRECTED")).GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(0,(await Read(client,Route+"?reviewState=UNREVIEWED")).GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1,(await Read(client,Route+"?reviewState=REVIEWED")).GetProperty("counts").GetProperty("total").GetInt32());
    }
    [Fact] public async Task Duplicate_observations_keep_independent_decisions_and_report_conflict_without_propagation()
    {
        using var host=Host();using var client=Client(host);
        foreach(var run in new[]{"a","b","c"}){using var response=await client.PostAsJsonAsync(Route+"/import",Bundle() with{RunId=run});Assert.Equal(HttpStatusCode.OK,response.StatusCode);}
        var rows=(await Read(client,Route)).GetProperty("results").GetProperty("items").EnumerateArray().ToArray();
        for(var index=0;index<2;index++)
        {
            var id=rows[index].GetProperty("id").GetGuid();
            using var response=await client.PostAsJsonAsync($"{Route}/{id}/reviews",new{expectedVersion=0,action=index==0?"CONFIRMED":"CORRECTED",
                contentStatus=index==0?(string?)null:"INVALID_RECRUITMENT",targetStatus=index==0?(string?)null:"NOT_MATCHED",notes="synthetic review"});
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        }
        var metrics=await Read(client,Route+"/quality-metrics");
        Assert.Equal(3,metrics.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1,metrics.GetProperty("counts").GetProperty("unreviewed").GetInt32());
        var unique=metrics.GetProperty("uniqueContentLevel").GetProperty("contentValidity");
        Assert.Equal(1,unique.GetProperty("humanConflicts").GetInt32());Assert.Equal(0,unique.GetProperty("eligibleSamples").GetInt32());
        Assert.Equal(2,metrics.GetProperty("observationLevel").GetProperty("contentValidity").GetProperty("eligibleSamples").GetInt32());
    }
    private WebApplicationFactory<Program> Host()
    {
        var name=Guid.NewGuid().ToString();return root.WithWebHostBuilder(b=>b.ConfigureServices(s=>{
            s.RemoveAll<IApplicationDbContext>();s.RemoveAll<IHostedService>();
            s.AddScoped<IApplicationDbContext>(_=>new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name).Options));
            s.ConfigureHttpClientDefaults(c=>c.ConfigurePrimaryHttpMessageHandler(()=>new DenyNetwork()));
        }));
    }
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client=host.CreateClient();using var scope=host.Services.CreateScope();
        var token=scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId,"admin@example.com");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.Value);return client;
    }
    private static async Task<JsonElement> Read(HttpClient client,string url)
    {using var response=await client.GetAsync(url);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");}
    private static DiscoveryReviewImport Bundle()
    {
        const string text="Hiring Senior Java Developer in Hanoi";
        return new(1,"SOCIALAPIS","synthetic-hardening","Backend",1,[new(0,"123",null,text,DiscoveryReviewRules.Hash(text),DiscoveryReviewRules.Hash(text),null,
            new("VALID_RECRUITMENT",["EMPLOYER_HIRING"],"Hiring",[text]),new("NOT_MATCHED",["SENIORITY_MISMATCH"],"Mismatch",[]),"NOT_CHECKED",
            JsonSerializer.SerializeToElement(new{}),[],[],"content-first-v1",DateTimeOffset.Parse("2026-10-26T03:00:00Z"))]);
    }
    private sealed class DenyNetwork:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>throw new InvalidOperationException("NETWORK_FORBIDDEN");}
}
