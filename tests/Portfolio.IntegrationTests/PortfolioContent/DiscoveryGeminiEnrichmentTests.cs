using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryGeminiEnrichmentTests(AuthApiFactory root):IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData("NEEDS_REVIEW")] [InlineData("INVALID_RECRUITMENT")] [InlineData("MISSING")]
    public async Task Backend_rejects_ineligible_classification_before_reservation(string classification)
    {
        using var s=await RecoveryScenario.Create(root,1);var item=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var llm=new Generator();var service=Service(db,llm);var e=await db.DiscoveryExecutions.SingleAsync();
            if(classification!="MISSING")await service.ProcessAsync(s.Id,new(0,item.ContentHash,item with {ContentValidity=item.ContentValidity with {Status=classification}},[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            await Assert.ThrowsAsync<ConflictException>(()=>service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default));
            Assert.Equal(0,llm.Calls);Assert.Equal(0,e.SemanticAttempts);
        });s.AssertOffline();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Completed_cache_is_reused_and_unknown_is_never_retried(bool fail)
    {
        using var s=await RecoveryScenario.Create(root,1);var item=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var llm=new Generator(fail);var service=Service(db,llm);var e=await db.DiscoveryExecutions.SingleAsync();
            var review=item with {ContentValidity=item.ContentValidity with {Status="VALID_RECRUITMENT"}};
            await service.ProcessAsync(s.Id,new(0,item.ContentHash,review,[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            await service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default);
            var version=e.Version;
            if(fail)await Assert.ThrowsAsync<ConflictException>(()=>service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default));
            else await service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default);
            Assert.Equal(version,e.Version);Assert.Equal(1,llm.Calls);Assert.Equal(1,e.SemanticAttempts);
            Assert.Equal(fail?"UNKNOWN":"COMPLETED",(await db.DiscoveryExecutionObservations.SingleAsync()).SemanticState);
            Assert.Null(e.ResultHash);Assert.NotNull(e.ActiveKey);Assert.Empty(await db.JobPostings.ToArrayAsync());
            Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsDeducted);
        });s.AssertOffline();
    }
    private static DiscoveryExecutionService Service(Portfolio.Application.Common.Abstractions.Persistence.IApplicationDbContext db,Generator generator)=>
        new(db,new NoGitHub(),generator,Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=true,GeminiFreeTierConfirmed=true}),new Actor(),TimeProvider.System);
    [Fact]
    public async Task Seven_saved_posts_respect_three_attempt_budget_without_changing_provider_evidence()
    {
        using var s=await RecoveryScenario.Create(root);var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var llm=new Generator();var service=Service(db,llm);var e=await db.DiscoveryExecutions.SingleAsync();
            foreach(var o in await db.DiscoveryExecutionObservations.OrderBy(x=>x.ItemIndex).ToArrayAsync()){
                using var doc=System.Text.Json.JsonDocument.Parse(o.PayloadJson);var source=doc.RootElement;
                var raw=source.GetProperty("rawContent").GetString()!;
                var review=template with {ItemIndex=o.ItemIndex,ProviderPostId=source.GetProperty("providerPostId").GetString(),SourceUrl=source.GetProperty("sourceUrl").GetString(),RawContent=raw,ContentHash=o.ContentHash,
                    ContentGroupHash=DiscoveryReviewRules.Hash(raw),ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"}};
                await service.ProcessAsync(s.Id,new(o.ItemIndex,o.ContentHash,review,[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
                var request=new DiscoveryPersistedSemanticRequest(o.ItemIndex,raw,o.ContentHash!,e.Version,"10001",true);
                if(o.ItemIndex<3)await service.ExtractPersistedAsync(s.Id,request,default);
                else await Assert.ThrowsAsync<ConflictException>(()=>service.ExtractPersistedAsync(s.Id,request,default));
            }
            Assert.Equal(3,llm.Calls);Assert.Equal(3,e.SemanticAttempts);Assert.Equal(7,await db.DiscoveryExecutionObservations.CountAsync());
            Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).ActualRequests);Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsDeducted);
            Assert.Empty(await db.JobPostings.ToArrayAsync());
        });s.AssertOffline();
    }
    private sealed class Generator(bool fail=false):IDiscoveryTextExtractor
    {
        public int Calls;public string Output="{}";public string ModelIdentity=>"offline-mock-v1";
        public Task<DiscoveryTextGeneration> ExtractAsync(string raw,CancellationToken ct){Calls++;if(fail)throw new InvalidOperationException("SYNTHETIC_TIMEOUT");return Task.FromResult(new DiscoveryTextGeneration(ModelIdentity,"ai-job-extraction-v1/ai-extraction-prompt-v1",Output,1,1));}
    }
    private sealed class NoGitHub:IDiscoveryGitHubClient
    {
        public Task<string> DispatchAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("NO_DISPATCH");
        public Task<bool> VerifyRunAsync(Guid id,string run,CancellationToken ct)=>throw new InvalidOperationException("NO_NETWORK");
    }
    private sealed class Actor:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>AuthApiFactory.AdminId;public string? Email=>null;}
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Gemini_origin_requires_generated_fact_and_persists_into_unified_read(bool generated)
    {
        using var s=await RecoveryScenario.Create(root,1);var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var raw=template.RawContent!;var value="Synthetic employer 0";
            var evidence=new[]{new {start=0,end=value.Length,quote=value}};
            var llm=new Generator{Output=generated?System.Text.Json.JsonSerializer.Serialize(new{schemaVersion="ai-job-extraction-v1",employer=new{value,status="POST_MENTIONED",evidence},vacancies=System.Array.Empty<object>()}):"{}"};
            var service=Service(db,llm);var e=await db.DiscoveryExecutions.SingleAsync();
            var review=template with {ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"}};
            await service.ProcessAsync(s.Id,new(0,template.ContentHash,review,[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            await service.ExtractPersistedAsync(s.Id,new(0,raw,template.ContentHash!,e.Version,"10001",true),default);
            var vacancy=System.Text.Json.JsonSerializer.SerializeToElement(new{companyName=value,fields=new{companyName=new{value,status="EXTRACTED",origin="GEMINI",evidence}}});
            await service.ProcessAsync(s.Id,new(0,template.ContentHash,review,[vacancy],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            var read=await new GetDiscoveryOpportunitiesHandler(db).HandleAsync(new(new(),s.Id,0));
            var o=Assert.IsType<DiscoveryObservationRead>(Assert.Single(read.Results.Items));
            var field=o.Vacancies[0].GetProperty("fields").GetProperty("companyName");
            Assert.Equal(generated?"GEMINI":"UNSPECIFIED",field.GetProperty("origin").GetString());
            Assert.Equal(generated?"EXTRACTED":"NEEDS_REVIEW",field.GetProperty("status").GetString());
            Assert.Equal(raw,o.Source.GetProperty("rawContent").GetString());Assert.Equal("VALID_RECRUITMENT",o.Review!.ContentValidity.Status);
            Assert.Empty(await db.JobPostings.ToArrayAsync());
        });s.AssertOffline();
    }
}
