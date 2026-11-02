using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Node worker -> real processing -> stored JSON -> real unified projection.
// EF InMemory verifies contracts; it does NOT verify PostgreSQL transactions.
public sealed class DiscoveryEvidenceV2Tests
{
    private static readonly Guid Id=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("exact")] [InlineData("corrected")] [InlineData("bytes")] [InlineData("inline")]
    [InlineData("multi")] [InlineData("three-roles")] [InlineData("schema-quotes")] [InlineData("cross")] [InlineData("ambiguous")] [InlineData("fabricated")]
    [InlineData("missing")] [InlineData("unknown")]
    [InlineData("conflict")] [InlineData("rejected")]
    public async Task Offline_Node_backend_persistence_and_unified_cards(string scenario)
    {
        var wire=await Capture(scenario,"V2");await using var db=Database();var service=Service(db);
        await Seed(db,service,wire,"V2",scenario);
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();var generation=observation.SemanticOutputJson;
        var raw=await db.RawJobPostings.Select(x=>x.RawContent).SingleAsync();
        var request=wire.GetProperty("processing").EnumerateArray().Last().Deserialize<DiscoveryProcessingRequest>(Json)!;
        await service.ProcessAsync(Id,request,"10001",default);
        var version=(await db.DiscoveryExecutions.SingleAsync()).Version;
        await service.ProcessAsync(Id,request,"10001",default);
        Assert.Equal(version,(await db.DiscoveryExecutions.SingleAsync()).Version);
        var projection=await new GetDiscoveryOpportunitiesHandler(db).HandleAsync(new(new GetDiscoveryReviewQuery(),Id,0));
        var card=JsonSerializer.SerializeToElement(Assert.Single(projection.Results.Items),Json);
        Assert.Equal(scenario=="unknown"?"UNKNOWN":"COMPLETED",card.GetProperty("semanticState").GetString());
        var fields=card.GetProperty("vacancies").EnumerateArray().Select(v=>v.GetProperty("fields")).ToArray();
        Assert.NotEmpty(fields);
        if(scenario is "exact" or "corrected" or "bytes" or "inline" or "schema-quotes"){
            Assert.Equal("TAX NET",fields[0].GetProperty("companyName").GetProperty("value").GetString());
            Assert.Equal("35 Lê Văn Thiêm 😀",fields[0].GetProperty("location").GetProperty("value").GetString());
            Assert.Equal("GEMINI",fields[0].GetProperty("location").GetProperty("origin").GetString());
            Assert.Equal("C#",fields[0].GetProperty("mandatorySkills").GetProperty("value")[0].GetString());
            Assert.Equal("2026-10-01",fields[0].GetProperty("mentionedPublishedAt").GetProperty("value").GetString());
        }
        if(scenario=="schema-quotes"){
            Assert.Equal("1 năm kinh nghiệm",fields[0].GetProperty("experience").GetProperty("value").GetString());
            Assert.Equal("GEMINI",fields[0].GetProperty("experience").GetProperty("origin").GetString());
            Assert.Equal("thực tập sinh",fields[0].GetProperty("seniority").GetProperty("value").GetString());
            Assert.Equal("GEMINI",fields[0].GetProperty("seniority").GetProperty("origin").GetString());
            Assert.Equal("Hiring",fields[0].GetProperty("recruitmentStatus").GetProperty("value").GetString());
            Assert.Equal("GEMINI",fields[0].GetProperty("recruitmentStatus").GetProperty("origin").GetString());
        }
        if(scenario is "ambiguous" or "missing" or "unknown")Assert.All(fields,f=>Assert.NotEqual("GEMINI",f.GetProperty("location").GetProperty("origin").GetString()));
        if(scenario=="conflict"){
            Assert.Equal("Original Labs",fields[0].GetProperty("companyName").GetProperty("value").GetString());
            Assert.Equal("DETERMINISTIC",fields[0].GetProperty("companyName").GetProperty("origin").GetString());
            Assert.True(fields[0].GetProperty("companyName").GetProperty("conflict").GetBoolean());
        }
        if(scenario=="rejected")Assert.All(fields.SelectMany(f=>f.EnumerateObject()),p=>Assert.NotEqual("GEMINI",p.Value.GetProperty("origin").GetString()));
        Assert.DoesNotContain("Fabricated Labs",card.GetRawText());
        if(scenario=="multi")Assert.Equal(2,fields.Length);
        if(scenario=="three-roles"){
            Assert.Equal(3,fields.Length);
            Assert.Equal("Intern Backend Developer",fields[0].GetProperty("positionTitle").GetProperty("value").GetString());
            Assert.Equal("Intern Frontend Developer",fields[1].GetProperty("positionTitle").GetProperty("value").GetString());
            Assert.Equal("Intern QA Tester",fields[2].GetProperty("positionTitle").GetProperty("value").GetString());
            Assert.Equal(new[]{".NET","C#"},fields[0].GetProperty("technologyStack").GetProperty("value").EnumerateArray().Select(x=>x.GetString()));
            Assert.Equal(new[]{"React"},fields[1].GetProperty("technologyStack").GetProperty("value").EnumerateArray().Select(x=>x.GetString()));
            Assert.Equal(new[]{"Postman"},fields[2].GetProperty("technologyStack").GetProperty("value").EnumerateArray().Select(x=>x.GetString()));
        }
        foreach(var f in fields.SelectMany(f=>f.EnumerateObject()).Select(p=>p.Value))foreach(var e in f.GetProperty("evidence").EnumerateArray())
            Assert.Equal(e.GetProperty("quote").GetString(),raw[e.GetProperty("start").GetInt32()..e.GetProperty("end").GetInt32()]);
        Assert.Equal(generation,observation.SemanticOutputJson);Assert.Equal(raw,(await db.RawJobPostings.SingleAsync()).RawContent);
        Assert.Equal(scenario=="unknown"?0:1,wire.GetProperty("cacheReads").GetInt32());
        var execution=await db.DiscoveryExecutions.SingleAsync();Assert.Equal(3,execution.SemanticAttempts);Assert.Equal("INGESTING",execution.State);
        Assert.NotNull(execution.ActiveKey);Assert.Null(execution.ResultHash);Assert.Empty(await db.DiscoveryExecutionVacancies.ToListAsync());Assert.Empty(await db.JobPostings.ToListAsync());
        var attempt=await db.DiscoveryExecutionAttempts.SingleAsync();Assert.Equal(1,attempt.ActualRequests);Assert.Equal(1,attempt.CreditsDeducted);
    }

    [Theory]
    [InlineData("value")] [InlineData("quote")] [InlineData("offset")] [InlineData("generation")]
    [InlineData("outer-value")] [InlineData("ambiguous")]
    public async Task Backend_independently_rejects_forged_corrected_evidence(string defect)
    {
        var wire=await Capture("corrected","V2");await using var db=Database();var service=Service(db);await Seed(db,service,wire,"V2","corrected");
        var request=JsonNode.Parse(wire.GetProperty("processing")[0].GetRawText())!;
        var vacancy=request["vacancies"]![0]!;var field=vacancy["fields"]!["location"]!;
        if(defect=="value"){field["value"]="TAX NET";vacancy["location"]="TAX NET";}
        if(defect=="outer-value")vacancy["location"]="TAX NET";
        if(defect=="quote")field["evidence"]![0]!["quote"]="Invented quotation";
        if(defect=="offset")field["evidence"]![0]!["start"]=0;
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();
        if(defect=="generation")observation.SemanticOutputJson=null;
        if(defect=="ambiguous"){
            // The model quote exists twice already (the word "NET" in employer
            // and .NET). The worker cannot select one corrected occurrence.
            var g=JsonSerializer.Deserialize<DiscoveryTextGeneration>(observation.SemanticOutputJson!,Json)!;
            var output=JsonNode.Parse(g.Output)!;output["sharedLocation"]!["value"]="NET";output["sharedLocation"]!["evidence"]![0]!["quote"]="NET";
            observation.SemanticOutputJson=JsonSerializer.Serialize(g with {Output=output.ToJsonString()},Json);
            field["value"]="NET";vacancy["location"]="NET";var start=wire.GetProperty("observation").GetProperty("rawContent").GetString()!.IndexOf("NET",StringComparison.Ordinal);
            field["evidence"]![0]!["quote"]="NET";field["evidence"]![0]!["start"]=start;field["evidence"]![0]!["end"]=start+3;
        }
        await db.SaveChangesAsync();await service.ProcessAsync(Id,request.Deserialize<DiscoveryProcessingRequest>(Json)!,"10001",default);
        using var stored=JsonDocument.Parse(observation.PayloadJson);
        var rejected=stored.RootElement.GetProperty("processing").GetProperty("vacancies")[0].GetProperty("fields").GetProperty("location");
        Assert.Equal(JsonValueKind.Null,rejected.GetProperty("value").ValueKind);Assert.Equal("NEEDS_REVIEW",rejected.GetProperty("status").GetString());
    }

    [Theory] [InlineData("exact",true)] [InlineData("corrected",false)]
    public async Task Strict_preserves_original_offset_behavior(string scenario,bool accepted)
    {
        var wire=await Capture(scenario,"STRICT");await using var db=Database();var service=Service(db);await Seed(db,service,wire,"STRICT",scenario);
        await service.ProcessAsync(Id,wire.GetProperty("processing")[0].Deserialize<DiscoveryProcessingRequest>(Json)!,"10001",default);
        using var doc=JsonDocument.Parse((await db.DiscoveryExecutionObservations.SingleAsync()).PayloadJson);
        var vacancies=doc.RootElement.GetProperty("processing").GetProperty("vacancies");
        Assert.Equal(accepted,vacancies.EnumerateArray().Any(v=>v.GetProperty("fields").GetProperty("companyName").GetProperty("origin").GetString()=="GEMINI"));
    }

    [Fact] public async Task Client_cannot_enable_V2_and_version_conflicts_remain_enforced()
    {
        Assert.Equal("STRICT",new DiscoveryExecutionOptions().EvidencePolicy);
        var wire=await Capture("corrected","V2");await using var db=Database();var service=Service(db);await Seed(db,service,wire,"STRICT","corrected");
        var request=wire.GetProperty("processing")[0].Deserialize<DiscoveryProcessingRequest>(Json)!;
        await Assert.ThrowsAsync<ConflictException>(()=>service.ProcessAsync(Id,request,"10001",default));
        request=request with {EvidencePolicy="STRICT",ExpectedVersion=-1,ExpectedWorkflowRunId="10001"};
        await Assert.ThrowsAsync<ConflictException>(()=>service.ProcessAsync(Id,request,null,default));
    }

    [Fact] public async Task Backend_rejects_original_model_cross_vacancy_claim_even_if_worker_forges_acceptance()
    {
        var wire=await Capture("cross","V2");await using var db=Database();var service=Service(db);await Seed(db,service,wire,"V2","cross");
        var request=JsonNode.Parse(wire.GetProperty("processing")[0].GetRawText())!;
        var v=request["vacancies"]![0]!;v["technologyStack"]=JsonSerializer.SerializeToNode(new[]{"React"});
        var raw=wire.GetProperty("observation").GetProperty("rawContent").GetString()!;var start=raw.IndexOf("React",StringComparison.Ordinal);
        v["fields"]!["technologyStack"]=JsonSerializer.SerializeToNode(new{value=new[]{"React"},status="EXTRACTED",origin="GEMINI",evidence=new[]{new{start,end=start+5,quote="React"}}});
        await service.ProcessAsync(Id,request.Deserialize<DiscoveryProcessingRequest>(Json)!,"10001",default);
        using var doc=JsonDocument.Parse((await db.DiscoveryExecutionObservations.SingleAsync()).PayloadJson);
        var f=doc.RootElement.GetProperty("processing").GetProperty("vacancies")[0].GetProperty("fields").GetProperty("technologyStack");
        Assert.Equal(JsonValueKind.Null,f.GetProperty("value").ValueKind);
    }

    [Fact] public async Task Completed_generation_cache_does_not_spend_another_attempt_when_processing_policy_changes()
    {
        var wire=await Capture("corrected","V2");await using var db=Database();var service=Service(db);await Seed(db,service,wire,"V2","corrected");
        await service.ProcessAsync(Id,wire.GetProperty("processing")[0].Deserialize<DiscoveryProcessingRequest>(Json)!,"10001",default);
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();var original=observation.SemanticOutputJson;
        observation.SemanticCacheKey=DiscoveryReviewRules.Hash(JsonSerializer.Serialize(new SortedDictionary<string,string>{
            ["contentHash"]=observation.ContentHash!,["contract"]="ai-job-extraction-v1/ai-extraction-prompt-v1",["model"]="offline-mock-v1"},Json));
        await db.SaveChangesAsync();
        var configured=new DiscoveryExecutionService(db,new GitHub(),new Semantic(),Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=true,GeminiFreeTierConfirmed=true,EvidencePolicy="STRICT"}),new Actor(),TimeProvider.System);
        var execution=await db.DiscoveryExecutions.SingleAsync();var version=execution.Version;
        var o=wire.GetProperty("observation");
        var request=new DiscoveryPersistedSemanticRequest(0,o.GetProperty("rawContent").GetString()!,observation.ContentHash!,version,"10001",true);
        await configured.ExtractPersistedAsync(Id,request,default);await configured.ExtractPersistedAsync(Id,request,default);
        Assert.Equal(3,execution.SemanticAttempts);Assert.Equal(version,execution.Version);Assert.Equal("COMPLETED",observation.SemanticState);Assert.Equal(original,observation.SemanticOutputJson);
    }

    [Theory]
    [InlineData("verified-hiring")]
    [InlineData("negated-hiring")]
    [InlineData("ambiguous-hiring")]
    [InlineData("role-local-hiring-in-multi")]
    [InlineData("negated-seniority")]
    [InlineData("ambiguous-seniority")]
    [InlineData("mismatched-experience-number")]
    [InlineData("optional-experience")]
    [InlineData("conflicting-experience")]
    public async Task SchemaQuoteSupported_rejects_adversarial_evidence_laundering(string attack)
    {
        var wire=await Capture(attack=="role-local-hiring-in-multi"?"multi":"schema-quotes","V2");
        await using var db=Database();var service=Service(db);
        await Seed(db,service,wire,"V2",attack=="role-local-hiring-in-multi"?"multi":"schema-quotes");
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();
        var raw=wire.GetProperty("observation").GetProperty("rawContent").GetString()!;
        var g=JsonSerializer.Deserialize<DiscoveryTextGeneration>(observation.SemanticOutputJson!,Json)!;
        var output=JsonNode.Parse(g.Output)!;
        var request=JsonNode.Parse(wire.GetProperty("processing").EnumerateArray().Last().GetRawText())!;
        var v0=request["vacancies"]![0]!;
        var fields0=v0["fields"]!;

        void ReplaceRaw(string oldText,string newText){
            raw=raw.Replace(oldText,newText,StringComparison.Ordinal);
            var hash=DiscoveryReviewRules.Hash(raw);
            observation.ContentHash=hash;
            var payload=JsonNode.Parse(observation.PayloadJson)!;
            payload["rawContent"]=raw;payload["contentHash"]=hash;
            var rawJson=payload.ToJsonString();
            using var doc=JsonDocument.Parse(rawJson);var s=doc.RootElement;
            observation.PayloadHash=DiscoveryReviewRules.Hash(JsonSerializer.Serialize(new SortedDictionary<string,object?>{
                ["contentHash"]=hash,["itemIndex"]=0,["providerPostId"]=s.GetProperty("providerPostId").GetString(),
                ["publishedAt"]=null,["rawContent"]=raw,["sourceUrl"]=s.GetProperty("sourceUrl").GetString()},Json));
            observation.PayloadJson=rawJson;
            request["contentHash"]=hash;
            request["review"]!["rawContent"]=raw;request["review"]!["contentHash"]=hash;
        }
        JsonNode Span(string quote){
            var idx=raw.IndexOf(quote,StringComparison.Ordinal);Assert.True(idx>=0,$"Missing quote: {quote}");
            return JsonSerializer.SerializeToNode(new[]{new{start=idx,end=idx+quote.Length,quote}},Json)!;
        }

        string targetField="recruitmentStatus";
        if(attack=="verified-hiring"){
            output["recruitmentStatus"]!["value"]="VERIFIED_HIRING";
            output["recruitmentStatus"]!["evidence"]=Span("Hiring");
            fields0["recruitmentStatus"]=JsonSerializer.SerializeToNode(new{value="Hiring",status="EXTRACTED",origin="GEMINI",evidence=Span("Hiring")},Json);
        }else if(attack=="negated-hiring"){
            ReplaceRaw("Benefits: Lunch","Benefits: Lunch\nNote: Không tuyển dụng");
            output["recruitmentStatus"]!["value"]="ANNOUNCED_UNVERIFIED";
            output["recruitmentStatus"]!["evidence"]=Span("Không tuyển dụng");
            fields0["recruitmentStatus"]=JsonSerializer.SerializeToNode(new{value="Không tuyển dụng",status="EXTRACTED",origin="GEMINI",evidence=Span("Không tuyển dụng")},Json);
        }else if(attack=="ambiguous-hiring"){
            ReplaceRaw("Benefits: Lunch","Benefits: Lunch\nNote: Previously hiring");
            output["recruitmentStatus"]!["value"]="ANNOUNCED_UNVERIFIED";
            output["recruitmentStatus"]!["evidence"]=Span("Previously hiring");
            fields0["recruitmentStatus"]=JsonSerializer.SerializeToNode(new{value="Previously hiring",status="EXTRACTED",origin="GEMINI",evidence=Span("Previously hiring")},Json);
        }else if(attack=="role-local-hiring-in-multi"){
            ReplaceRaw("Requirements: React","Requirements: React\nĐang tuyển thêm");
            output["recruitmentStatus"]=JsonSerializer.SerializeToNode(new{value="ANNOUNCED_UNVERIFIED",status="POST_MENTIONED",evidence=Span("Đang tuyển thêm")},Json);
            fields0["recruitmentStatus"]=JsonSerializer.SerializeToNode(new{value="Đang tuyển thêm",status="EXTRACTED",origin="GEMINI",evidence=Span("Đang tuyển thêm")},Json);
        }else if(attack=="negated-seniority"){
            targetField="seniority";
            ReplaceRaw("Level: thực tập sinh","Level: không nhận thực tập sinh");
            output["vacancies"]![0]!["seniority"]!["value"]="INTERN";
            output["vacancies"]![0]!["seniority"]!["evidence"]=Span("không nhận thực tập sinh");
            fields0["seniority"]=JsonSerializer.SerializeToNode(new{value="không nhận thực tập sinh",status="EXTRACTED",origin="GEMINI",evidence=Span("không nhận thực tập sinh")},Json);
        }else if(attack=="ambiguous-seniority"){
            targetField="seniority";
            ReplaceRaw("Level: thực tập sinh","Level: Intern hoặc Senior");
            output["vacancies"]![0]!["seniority"]!["value"]="INTERN";
            output["vacancies"]![0]!["seniority"]!["evidence"]=Span("Intern hoặc Senior");
            fields0["seniority"]=JsonSerializer.SerializeToNode(new{value="Intern hoặc Senior",status="EXTRACTED",origin="GEMINI",evidence=Span("Intern hoặc Senior")},Json);
        }else if(attack=="mismatched-experience-number"){
            targetField="experience";
            output["vacancies"]![0]!["experienceYears"]!["value"]=5;
            output["vacancies"]![0]!["experienceYears"]!["evidence"]=Span("1 năm kinh nghiệm");
            fields0["experience"]=JsonSerializer.SerializeToNode(new{value="1 năm kinh nghiệm",status="EXTRACTED",origin="GEMINI",evidence=Span("1 năm kinh nghiệm")},Json);
        }else if(attack=="optional-experience"){
            targetField="experience";
            ReplaceRaw("Experience: 1 năm kinh nghiệm","Experience: ưu tiên 1 năm kinh nghiệm");
            output["vacancies"]![0]!["experienceYears"]!["value"]=1;
            output["vacancies"]![0]!["experienceYears"]!["evidence"]=Span("ưu tiên 1 năm kinh nghiệm");
            fields0["experience"]=JsonSerializer.SerializeToNode(new{value="ưu tiên 1 năm kinh nghiệm",status="EXTRACTED",origin="GEMINI",evidence=Span("ưu tiên 1 năm kinh nghiệm")},Json);
        }else if(attack=="conflicting-experience"){
            targetField="experience";
            ReplaceRaw("Experience: 1 năm kinh nghiệm","Experience: 1 năm hoặc 3 năm kinh nghiệm");
            output["vacancies"]![0]!["experienceYears"]!["value"]=1;
            output["vacancies"]![0]!["experienceYears"]!["evidence"]=Span("1 năm hoặc 3 năm kinh nghiệm");
            fields0["experience"]=JsonSerializer.SerializeToNode(new{value="1 năm hoặc 3 năm kinh nghiệm",status="EXTRACTED",origin="GEMINI",evidence=Span("1 năm hoặc 3 năm kinh nghiệm")},Json);
        }

        observation.RawJobPostingId=null;
        observation.SemanticOutputJson=JsonSerializer.Serialize(g with {Output=output.ToJsonString()},Json);
        await db.SaveChangesAsync();

        var parsedRequest=request.Deserialize<DiscoveryProcessingRequest>(Json)!;
        var validReview=parsedRequest.Review! with {RawContent=raw,ContentHash=observation.ContentHash,ContentGroupHash=DiscoveryReviewRules.Hash(raw)};
        await service.ProcessAsync(Id,parsedRequest with {Review=validReview},"10001",default);
        using var stored=JsonDocument.Parse((await db.DiscoveryExecutionObservations.SingleAsync()).PayloadJson);
        var check=stored.RootElement.GetProperty("processing").GetProperty("vacancies")[0].GetProperty("fields").GetProperty(targetField);
        Assert.Equal(JsonValueKind.Null,check.GetProperty("value").ValueKind);
        Assert.Equal("UNSPECIFIED",check.GetProperty("origin").GetString());
    }

    [Fact] public async Task Historical_snapshot_without_evidencePolicy_remains_STRICT_even_when_live_option_is_V2()
    {
        var wire=await Capture("corrected","STRICT");await using var db=Database();
        var liveV2Service=new DiscoveryExecutionService(db,new GitHub(),new Semantic(),Options.Create(new DiscoveryExecutionOptions{EvidencePolicy="V2"}),new Actor(),TimeProvider.System);
        db.DiscoveryExecutions.Add(new(){Id=Id,ClientRequestId=Guid.NewGuid(),State="CLAIMED",WorkflowRunId="10001",SemanticAttempts=3,ConfigurationJson="{}"});
        db.DiscoveryExecutionAttempts.Add(new(){Id=Id,ExecutionId=Id,WorkflowRunId="10001"});await db.SaveChangesAsync();
        await liveV2Service.ObservationsAsync(Id,"10001",new(1,1,null,200,[wire.GetProperty("observation")],ProviderOperation:"SUCCEEDED"),default);
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();observation.SemanticState="COMPLETED";
        observation.SemanticOutputJson=wire.GetProperty("generation").GetRawText();await db.SaveChangesAsync();

        var strictRequest=wire.GetProperty("processing")[0].Deserialize<DiscoveryProcessingRequest>(Json)!;
        await Assert.ThrowsAsync<ConflictException>(()=>liveV2Service.ProcessAsync(Id,strictRequest with {EvidencePolicy="V2"},"10001",default));
        await liveV2Service.ProcessAsync(Id,strictRequest,"10001",default);
        using var doc=JsonDocument.Parse((await db.DiscoveryExecutionObservations.SingleAsync()).PayloadJson);
        Assert.Equal("STRICT",doc.RootElement.GetProperty("processing").GetProperty("evidencePolicy").GetString());
        Assert.All(doc.RootElement.GetProperty("processing").GetProperty("vacancies").EnumerateArray(),v=>Assert.NotEqual("GEMINI",v.GetProperty("fields").GetProperty("companyName").GetProperty("origin").GetString()));
    }

    private static ContentTestDbContext Database()=>new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DiscoveryExecutionService Service(ContentTestDbContext db)=>new(db,new GitHub(),new Semantic(),Options.Create(new DiscoveryExecutionOptions()),new Actor(),TimeProvider.System);
    private static async Task Seed(ContentTestDbContext db,DiscoveryExecutionService service,JsonElement wire,string policy,string scenario)
    {
        db.DiscoveryExecutions.Add(new(){Id=Id,ClientRequestId=Guid.NewGuid(),State="CLAIMED",WorkflowRunId="10001",SemanticAttempts=3,ConfigurationJson=JsonSerializer.Serialize(new{evidencePolicy=policy})});
        db.DiscoveryExecutionAttempts.Add(new(){Id=Id,ExecutionId=Id,WorkflowRunId="10001"});await db.SaveChangesAsync();
        await service.ObservationsAsync(Id,"10001",new(1,1,null,200,[wire.GetProperty("observation")],ProviderOperation:"SUCCEEDED"),default);
        var observation=await db.DiscoveryExecutionObservations.SingleAsync();observation.SemanticState=scenario=="unknown"?"UNKNOWN":"COMPLETED";
        observation.SemanticOutputJson=scenario=="unknown"?null:wire.GetProperty("generation").GetRawText();await db.SaveChangesAsync();
    }
    private sealed class GitHub:IDiscoveryGitHubClient{public Task<string> DispatchAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("NO_DISPATCH");public Task<bool> VerifyRunAsync(Guid id,string run,CancellationToken ct)=>Task.FromResult(run=="10001");}
    private sealed class Semantic:IDiscoveryTextExtractor{public string ModelIdentity=>"offline-mock-v1";public Task<DiscoveryTextGeneration> ExtractAsync(string raw,CancellationToken ct)=>throw new InvalidOperationException("LIVE_GEMINI_FORBIDDEN");}
    private sealed class Actor:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>null;public string? Email=>null;}
    private static async Task<JsonElement> Capture(string scenario,string policy)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"Portfolio.sln")))root=root.Parent;
        Assert.NotNull(root);var start=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,UseShellExecute=false};
        start.ArgumentList.Add("tools/browser-job-discovery/test/fixtures/evidence-production-v2.mjs");start.ArgumentList.Add(scenario);start.ArgumentList.Add(policy);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);Assert.True(process.ExitCode==0,await errors);
        return JsonDocument.Parse(await output).RootElement.Clone();
    }
}
