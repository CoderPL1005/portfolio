using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Real worker -> JSON callback DTO -> real ingestion handler, fake HTTP only.
public sealed class DiscoveryExecutionCorrectionTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData(true,true,true,1)] [InlineData(false,true,true,0)]
    [InlineData(true,false,true,0)] [InlineData(true,true,false,0)]
    public async Task Admin_replay_uses_current_consent_and_budget_without_rewriting_frozen_configuration(bool enabled,bool free,bool consent,int calls)
    {
        var wire=await Capture("incomplete");await using var db=Database();await Seed(db);
        var execution=await db.DiscoveryExecutions.SingleAsync();execution.ConfigurationJson="{\"semanticEnabled\":false}";await db.SaveChangesAsync();
        var semantic=new CountingSemantic();
        var service=new DiscoveryExecutionService(db,new GitHub(),semantic,Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=enabled,GeminiFreeTierConfirmed=free}),new Actor(),TimeProvider.System);
        var saved=wire.GetProperty("saved").Deserialize<DiscoveryExecutionResult>(Json)!;
        await service.ObservationsAsync(Id,"10001",saved,default);
        var classified=wire.GetProperty("result").Deserialize<DiscoveryExecutionResult>(Json)!.ReviewBundle!.Items[0];
        await service.ProcessAsync(Id,new(0,classified.ContentHash,classified,[],ExpectedVersion:execution.Version,ExpectedWorkflowRunId:"10001"),null,default);
        var o=saved.Observations[0];var request=new DiscoveryPersistedSemanticRequest(0,o.GetProperty("rawContent").GetString()!,o.GetProperty("contentHash").GetString()!,execution.Version,"10001",consent);
        if(calls==0)await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ConflictException>(()=>service.ExtractPersistedAsync(Id,request,default));
        else {
            await service.ExtractPersistedAsync(Id,request,default); // Synthetic failure is durably UNKNOWN, never retried.
            await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ConflictException>(()=>service.ExtractPersistedAsync(Id,request with {ExpectedVersion=execution.Version},default));
        }
        Assert.Equal(calls,semantic.Calls);Assert.Equal(calls,execution.SemanticAttempts);
        Assert.Equal("{\"semanticEnabled\":false}",execution.ConfigurationJson);Assert.Equal("INGESTING",execution.State);Assert.Null(execution.ResultHash);
        Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsDeducted);Assert.Empty(await db.JobPostings.ToListAsync());
    }
    [Theory]
    [InlineData("provider-error","FAILED",0)]
    [InlineData("contradictory","PROVIDER_UNKNOWN",0)]
    [InlineData("unknown","PROVIDER_UNKNOWN",0)]
    [InlineData("invalid-url","FAILED",0)]
    [InlineData("incomplete","PARTIAL",0)]
    [InlineData("success","PARTIAL",0)]
    [InlineData("zero","PARTIAL",0)]
    [InlineData("multi","PARTIAL",0)]
    public async Task Worker_callback_preserves_failure_promotion_and_evidence(string scenario,string expected,int jobs)
    {
        var wire=await Capture(scenario);var result=wire.GetProperty("result").Deserialize<DiscoveryExecutionResult>(Json)!;
        await using var db=Database();var service=Service(db);await Seed(db);
        await service.ObservationsAsync(Id,"10001",wire.GetProperty("saved").Deserialize<DiscoveryExecutionResult>(Json)!,default);
        await service.CompleteAsync(Id,"10001",result,default);
        Assert.Equal(expected,(await db.DiscoveryExecutions.SingleAsync()).State);
        Assert.Equal(jobs,await db.JobPostings.CountAsync());Assert.Single(await db.DiscoveryExecutionAttempts.ToListAsync());
        if(scenario is "success" or "zero" or "multi"){
            var staged=await db.DiscoveryExecutionVacancies.ToListAsync();
            Assert.Equal(scenario=="multi"?2:1,staged.Count);
            Assert.All(staged,v=>{Assert.Null(v.JobPostingId);Assert.Equal("NEEDS_REVIEW",v.Status);});
        }
        Assert.Equal(1,(await db.DiscoveryExecutionAttempts.SingleAsync()).ActualRequests);
        Assert.Equal(6m,(await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsUsedAlternateHeader);
        if(expected=="PROVIDER_UNKNOWN")Assert.NotNull((await db.DiscoveryExecutions.SingleAsync()).ActiveKey);
        if(scenario=="provider-error")Assert.Equal("PROVIDER_ERROR",(await db.DiscoveryExecutions.SingleAsync()).ErrorCode);
        if(scenario=="invalid-url")Assert.Equal("https://example.invalid/not-facebook",JsonDocument.Parse((await db.DiscoveryExecutionObservations.SingleAsync()).PayloadJson).RootElement.GetProperty("providerSourceUrl").GetString());
        if(scenario=="contradictory")Assert.Contains(await db.DiscoveryExecutionEvents.ToListAsync(),e=>e.Reason!=null&&e.Reason.Contains("bodyCreditsCharged"));
        foreach(var row in await db.DiscoveryExecutionVacancies.ToListAsync())
        {
            var fields=JsonDocument.Parse(row.FieldsJson).RootElement;var stack=fields.GetProperty("technologyStack");
            if(stack.GetProperty("status").GetString()!="EXTRACTED")continue;
            var raw=result.Observations[0].GetProperty("rawContent").GetString()!;
            foreach(var technology in stack.GetProperty("value").EnumerateArray())
                Assert.Contains(stack.GetProperty("evidence").EnumerateArray(),e=>raw[e.GetProperty("start").GetInt32()..e.GetProperty("end").GetInt32()]==e.GetProperty("quote").GetString()&&string.Equals(e.GetProperty("quote").GetString(),technology.GetString(),StringComparison.OrdinalIgnoreCase));
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_or_fabricated_array_spans_cannot_remain_extracted(bool fabricated)
    {
        var wire=await Capture("success");var node=JsonNode.Parse(wire.GetProperty("result").GetRawText())!;
        node["observations"]![0]!["vacancies"]![0]!["fields"]!["technologyStack"]!["evidence"]=JsonNode.Parse(fabricated?"[{\"start\":0,\"end\":2,\"quote\":\"C#\"}]":"[]");
        await using var db=Database();await Seed(db);
        await Service(db).CompleteAsync(Id,"10001",node.Deserialize<DiscoveryExecutionResult>(Json)!,default);
        var field=JsonDocument.Parse((await db.DiscoveryExecutionVacancies.SingleAsync()).FieldsJson).RootElement.GetProperty("technologyStack");
        Assert.Equal("NEEDS_REVIEW",field.GetProperty("status").GetString());Assert.Equal(JsonValueKind.Null,field.GetProperty("value").ValueKind);
    }
    [Fact] public async Task Array_spans_from_another_vacancy_are_not_evidence_for_this_role()
    {
        var wire=await Capture("multi");var node=JsonNode.Parse(wire.GetProperty("result").GetRawText())!;
        var vacancies=node["observations"]![0]!["vacancies"]!;
        vacancies[0]!["technologyStack"]=JsonNode.Parse("[\"Python\"]");
        vacancies[0]!["fields"]!["technologyStack"]=vacancies[1]!["fields"]!["technologyStack"]!.DeepClone();
        await using var db=Database();await Seed(db);
        await Service(db).CompleteAsync(Id,"10001",node.Deserialize<DiscoveryExecutionResult>(Json)!,default);
        var row=await db.DiscoveryExecutionVacancies.SingleAsync(x=>x.VacancyIndex==0);
        Assert.Equal("NEEDS_REVIEW",JsonDocument.Parse(row.FieldsJson).RootElement.GetProperty("technologyStack").GetProperty("status").GetString());
    }
    [Fact] public async Task Fatal_cost_blocks_direct_semantic_endpoint_and_is_immutable()
    {
        var wire=await Capture("contradictory");var saved=wire.GetProperty("saved").Deserialize<DiscoveryExecutionResult>(Json)!;
        await using var db=Database();await Seed(db);var semantic=new CountingSemantic();
        var service=new DiscoveryExecutionService(db,new GitHub(),semantic,Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=true,GeminiFreeTierConfirmed=true}),new Actor(),TimeProvider.System);
        await service.ObservationsAsync(Id,"10001",saved,default);
        var o=saved.Observations[0];
        await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ConflictException>(()=>service.ExtractAsync(Id,"10001",new(0,o.GetProperty("rawContent").GetString()!,o.GetProperty("contentHash").GetString()!),default));
        Assert.Equal(0,semantic.Calls);
        var tampered=wire.GetProperty("result").Deserialize<DiscoveryExecutionResult>(Json)! with {UsageEvidence=new(false,JsonSerializer.SerializeToElement<object?>(null)),ProviderOperation="SUCCEEDED",ErrorCode=null,State="SUCCEEDED"};
        await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ConflictException>(()=>service.CompleteAsync(Id,"10001",tampered,default));
        Assert.Empty(await db.JobPostings.ToListAsync());
    }
    [Fact] public async Task Modern_callback_missing_outcome_does_not_use_legacy_promotion_compatibility()
    {
        var wire=await Capture("success");var node=JsonNode.Parse(wire.GetProperty("result").GetRawText())!;
        node["observations"]![0]!.AsObject().Remove("outcome");
        await using var db=Database();await Seed(db);await Service(db).CompleteAsync(Id,"10001",node.Deserialize<DiscoveryExecutionResult>(Json)!,default);
        Assert.Empty(await db.JobPostings.ToListAsync());Assert.Single(await db.DiscoveryExecutionObservations.ToListAsync());
        Assert.Equal("PARTIAL",(await db.DiscoveryExecutions.SingleAsync()).State);
    }
    [Fact] public async Task Transport_and_charge_alone_are_not_provider_success_proof()
    {
        await using var db=Database();await Seed(db);
        await Service(db).CompleteAsync(Id,"10001",new(1,1,6,200,[]),default);
        var e=await db.DiscoveryExecutions.SingleAsync();Assert.Equal("PROVIDER_UNKNOWN",e.State);Assert.NotNull(e.ActiveKey);
        Assert.Empty(await db.JobPostings.ToListAsync());
    }
    private static readonly Guid Id=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static ContentTestDbContext Database()=>new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task Seed(ContentTestDbContext db){db.DiscoveryExecutions.Add(new(){Id=Id,ClientRequestId=Guid.NewGuid(),State="CLAIMED",WorkflowRunId="10001"});db.DiscoveryExecutionAttempts.Add(new(){Id=Id,ExecutionId=Id,WorkflowRunId="10001"});await db.SaveChangesAsync();}
    private static DiscoveryExecutionService Service(ContentTestDbContext db)=>new(db,new GitHub(),new Semantic(),Options.Create(new DiscoveryExecutionOptions()),new Actor(),TimeProvider.System);
    private sealed class GitHub:IDiscoveryGitHubClient{public Task<string> DispatchAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("NO_DISPATCH");public Task<bool> VerifyRunAsync(Guid id,string run,CancellationToken ct)=>Task.FromResult(run=="10001");}
    private sealed class Semantic:IDiscoveryTextExtractor{public string ModelIdentity=>"synthetic";public Task<DiscoveryTextGeneration> ExtractAsync(string raw,CancellationToken ct)=>throw new InvalidOperationException("NO_LLM");}
    private sealed class CountingSemantic:IDiscoveryTextExtractor{public int Calls;public string ModelIdentity=>"synthetic";public Task<DiscoveryTextGeneration> ExtractAsync(string raw,CancellationToken ct){Calls++;throw new InvalidOperationException("NO_LLM");}}
    private sealed class Actor:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>null;public string? Email=>null;}
    private static async Task<JsonElement> Capture(string scenario)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"Portfolio.sln")))root=root.Parent;
        Assert.NotNull(root);var start=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        start.ArgumentList.Add("tools/browser-job-discovery/test/fixtures/execution-corrections.mjs");start.ArgumentList.Add(scenario);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);Assert.True(process.ExitCode==0,await errors);
        return JsonDocument.Parse(await output).RootElement.Clone();
    }
}
