using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryExecutionProcessingTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [Fact] public async Task Failed_classification_is_visible_without_a_prediction_or_promotion()
    {
        using var s=await RecoveryScenario.Create(root,count:1);var original=(await s.NormalResult()).ReviewBundle!.Items[0];
        var path=$"{RecoveryScenario.ReviewPath}/executions/{s.Id}";
        await RecoveryScenario.Data(await s.Client.PostAsJsonAsync(path+"/processing",new {itemIndex=0,contentHash=original.ContentHash,
            review=(object?)null,vacancies=System.Array.Empty<object>(),extractionError="CLASSIFICATION_FAILED",expectedVersion=s.Version,expectedWorkflowRunId="10001"}));
        var view=await RecoveryScenario.Data(await s.Client.GetAsync(path));
        var processing=view.GetProperty("results")[0].GetProperty("processing");
        Assert.Equal(JsonValueKind.Null,processing.GetProperty("review").ValueKind);
        Assert.Equal("CLASSIFICATION_FAILED",processing.GetProperty("extractionError").GetString());
        Assert.Equal(0,view.GetProperty("counts").GetProperty("validRecruitment").GetInt32());
        Assert.Equal("INGESTING",view.GetProperty("state").GetString());
        await s.Read(async db=>{Assert.Empty(await db.JobPostings.ToListAsync());Assert.Empty(await db.DiscoveryCandidates.ToListAsync());});s.AssertOffline();
    }
    [Theory]
    [InlineData("raw")] [InlineData("hash")] [InlineData("id")] [InlineData("date")]
    public async Task Processing_cannot_suppress_or_fabricate_source_fields(string field)
    {
        using var s=await RecoveryScenario.Create(root,count:1);
        var original=(await s.NormalResult()).ReviewBundle!.Items[0];
        var review=field switch {
            "raw"=>original with {RawContent=null,ContentHash=null,ContentGroupHash=null},
            "hash"=>original with {ContentHash=new string('0',64)},
            "id"=>original with {ProviderPostId=null},
            _=>original with {PublishedAt=DateTimeOffset.Parse("2026-01-01T00:00:00Z")}
        };
        var response=await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/executions/{s.Id}/processing",
            new {itemIndex=0,contentHash=original.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=s.Version,expectedWorkflowRunId="10001"});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        await s.Read(async db=>Assert.Equal(s.Version,(await db.DiscoveryExecutions.SingleAsync()).Version));
        s.AssertOffline();
    }
    [Fact] public async Task Seven_saved_posts_can_be_processed_twice_without_jobs_claims_or_accounting_changes()
    {
        using var s=await RecoveryScenario.Create(root);var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        var path=$"{RecoveryScenario.ReviewPath}/executions/{s.Id}";
        var view=await RecoveryScenario.Data(await s.Client.GetAsync(path));var version=s.Version;
        for(var pass=0;pass<2;pass++)foreach(var item in view.GetProperty("results").EnumerateArray()){
            var source=item.GetProperty("observation");var raw=source.GetProperty("rawContent").GetString()!;
            var review=template with {ItemIndex=source.GetProperty("itemIndex").GetInt32(),RawContent=raw,ContentHash=source.GetProperty("contentHash").GetString(),
                ContentGroupHash=Portfolio.Application.Features.JobHunting.DiscoveryReviewRules.Hash(raw.Normalize().Replace("\r\n","\n").Replace('\r','\n').Trim()),
                ProviderPostId=source.GetProperty("providerPostId").GetString(),SourceUrl=source.GetProperty("sourceUrl").GetString()};
            var response=await RecoveryScenario.Data(await s.Client.PostAsJsonAsync(path+"/processing",new {itemIndex=review.ItemIndex,contentHash=review.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=version,expectedWorkflowRunId="10001"}));
            version=response.GetProperty("version").GetInt32();
        }
        Assert.Equal(s.Version+7,version);
        await s.Read(async db=>{Assert.Equal(7,await db.DiscoveryExecutionObservations.CountAsync());Assert.Empty(await db.JobPostings.ToListAsync());Assert.Empty(await db.DiscoveryCandidates.ToListAsync());
            var attempt=Assert.Single(await db.DiscoveryExecutionAttempts.ToListAsync());Assert.Equal(1,attempt.ActualRequests);Assert.Equal(1,attempt.CreditsDeducted);Assert.Null((await db.DiscoveryExecutions.SingleAsync()).ResultHash);});s.AssertOffline();
    }
    [Fact] public async Task Persisted_processing_is_Admin_only_idempotent_and_does_not_complete_execution()
    {
        using var s=await RecoveryScenario.Create(root,count:1);
        var result=await s.NormalResult();var review=result.ReviewBundle!.Items[0];
        var request=new {itemIndex=0,contentHash=review.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=s.Version,expectedWorkflowRunId="10001"};
        var path=$"{RecoveryScenario.ReviewPath}/executions/{s.Id}/processing";
        using(var anonymous=s.Factory.CreateClient())Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync(path,request)).StatusCode);
        var first=await RecoveryScenario.Data(await s.Client.PostAsJsonAsync(path,request));
        var again=await RecoveryScenario.Data(await s.Client.PostAsJsonAsync(path,request));
        Assert.Equal(first.GetRawText(),again.GetRawText());
        var view=await RecoveryScenario.Data(await s.Client.GetAsync($"{RecoveryScenario.ReviewPath}/executions/{s.Id}"));
        Assert.Equal("INGESTING",view.GetProperty("state").GetString());
        Assert.Equal("NEEDS_REVIEW",view.GetProperty("results")[0].GetProperty("processing").GetProperty("review").GetProperty("contentValidity").GetProperty("status").GetString());
        Assert.Equal("NOT_REQUESTED",view.GetProperty("results")[0].GetProperty("semanticState").GetString());
        await s.Read(async db=>{Assert.Empty(await db.JobPostings.ToListAsync());Assert.Null((await db.DiscoveryExecutions.SingleAsync()).ResultHash);Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsDeducted);});
        s.AssertOffline();
    }
    [Theory] [InlineData("10002",false)] [InlineData("10001",true)]
    public async Task Replay_rejects_wrong_run_or_source(string run,bool tamper)
    {
        using var s=await RecoveryScenario.Create(root,count:1);var result=await s.NormalResult();var review=result.ReviewBundle!.Items[0];
        var body=new {itemIndex=0,contentHash=tamper?new string('0',64):review.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=s.Version,expectedWorkflowRunId=run};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/executions/{s.Id}/processing",body)).StatusCode);
        s.AssertOffline();
    }
    [Theory] [InlineData("missing-cost")] [InlineData("unknown-operation")]
    public async Task Replay_keeps_uncertain_accounting_blocked(string defect)
    {
        using var s=await RecoveryScenario.Create(root,count:1);var result=await s.NormalResult();var review=result.ReviewBundle!.Items[0];await s.MutateEvidence(defect);
        var body=new {itemIndex=0,contentHash=review.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=s.Version,expectedWorkflowRunId="10001"};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/executions/{s.Id}/processing",body)).StatusCode);s.AssertOffline();
    }
    [Fact] public async Task Stale_processing_and_Gemini_without_consent_are_blocked()
    {
        using var s=await RecoveryScenario.Create(root,count:1);var result=await s.NormalResult();var review=result.ReviewBundle!.Items[0];
        var path=$"{RecoveryScenario.ReviewPath}/executions/{s.Id}";
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client.PostAsJsonAsync(path+"/processing",new {itemIndex=0,contentHash=review.ContentHash,review,vacancies=System.Array.Empty<object>(),expectedVersion=s.Version-1,expectedWorkflowRunId="10001"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client.PostAsJsonAsync(path+"/extract-persisted",new {itemIndex=0,rawContent=review.RawContent,contentHash=review.ContentHash,expectedVersion=s.Version,expectedWorkflowRunId="10001"})).StatusCode);s.AssertOffline();
    }
}
