using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryUnifiedReadTests(AuthApiFactory root):IClassFixture<AuthApiFactory>
{
    [Fact] public async Task A_malformed_saved_review_remains_visible_and_pending_without_reclassification()
    {
        using var s=await RecoveryScenario.Create(root,1);
        await s.Read(async db=>{var o=await db.DiscoveryExecutionObservations.SingleAsync();var node=System.Text.Json.Nodes.JsonNode.Parse(o.PayloadJson)!;
            node["processing"]=System.Text.Json.Nodes.JsonNode.Parse("{\"review\":{\"unexpected\":true},\"vacancies\":[]}");o.PayloadJson=node.ToJsonString();await db.SaveChangesAsync();});
        var before=await s.Snapshot();var data=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true"));
        Assert.Equal(1,data.GetProperty("counts").GetProperty("unknown").GetInt32());
        var item=Assert.Single(data.GetProperty("results").GetProperty("items").EnumerateArray());Assert.Equal(JsonValueKind.Null,item.GetProperty("review").ValueKind);
        Assert.Contains(item.GetProperty("sourceWarnings").EnumerateArray(),x=>x.GetString()=="PERSISTED_REVIEW_UNREADABLE");
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Multiple_import_coordinates_for_one_observation_do_not_duplicate_display_or_propagate_labels()
    {
        using var s=await RecoveryScenario.Create(root);await s.AddNormalCandidate(reviewed:true,sameRun:true);
        await s.Read(async db=>{
            var c=await db.DiscoveryCandidates.SingleAsync();
            db.DiscoveryCandidates.Add(new(){Id=Guid.NewGuid(),RunId="10001",Query=c.Query,PageNumber=c.PageNumber,ItemIndex=c.ItemIndex,Payload=c.Payload,
                PayloadHash=c.PayloadHash,ContentStatus=c.ContentStatus,TargetStatus=c.TargetStatus,IngestedAt=c.IngestedAt});await db.SaveChangesAsync();
        });
        var before=await s.Snapshot();var data=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true"));
        Assert.Equal(7,data.GetProperty("counts").GetProperty("total").GetInt32());
        var candidate=Assert.Single(data.GetProperty("results").GetProperty("items").EnumerateArray().Where(x=>x.GetProperty("origin").GetString()=="CLASSIFIER"));
        Assert.Equal(2,candidate.GetProperty("observation").GetProperty("linkedCandidateIds").GetArrayLength());
        Assert.Equal("CORRECTED",candidate.GetProperty("humanReview").GetProperty("status").GetString());
        await s.Read(async db=>Assert.Single(await db.DiscoveryCandidateReviews.ToListAsync()));
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Theory] [InlineData("id")] [InlineData("hash")] [InlineData("url")] [InlineData("date")]
    public async Task Conflicting_provenance_does_not_merge_a_candidate_with_an_observation(string field)
    {
        using var s=await RecoveryScenario.Create(root);await s.AddNormalCandidate(reviewed:false,sameRun:true);
        await s.Read(async db=>{var c=await db.DiscoveryCandidates.SingleAsync();var item=JsonSerializer.Deserialize<DiscoveryReviewItem>(c.Payload,DiscoveryReviewRules.Json)!;
            item=field switch {"id"=>item with {ProviderPostId="pfbidUnverified"},"hash"=>item with {ContentHash=new string('0',64)},"date"=>item with {PublishedAt=DateTimeOffset.Parse("2026-01-01T00:00:00Z")},_=>item with {SourceUrl=null}};
            c.Payload=JsonSerializer.Serialize(item,DiscoveryReviewRules.Json);await db.SaveChangesAsync();});
        var before=await s.Snapshot();var data=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true"));
        Assert.Equal(8,data.GetProperty("counts").GetProperty("total").GetInt32());Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task An_observation_with_a_real_candidate_displays_once_and_keeps_its_Human_Review()
    {
        using var s=await RecoveryScenario.Create(root);await s.AddNormalCandidate(reviewed:true,sameRun:true);
        var before=await s.Snapshot();
        var data=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true"));
        Assert.Equal(7,data.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1,data.GetProperty("counts").GetProperty("reviewed").GetInt32());
        var candidate=Assert.Single(data.GetProperty("results").GetProperty("items").EnumerateArray().Where(x=>x.GetProperty("origin").GetString()=="CLASSIFIER"));
        Assert.Equal("CORRECTED",candidate.GetProperty("humanReview").GetProperty("status").GetString());
        Assert.Equal(s.Id,candidate.GetProperty("observation").GetProperty("executionId").GetGuid());
        var selected=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+$"?includeObservations=true&executionId={s.Id}&itemIndex=0"));
        Assert.Equal(candidate.GetProperty("id").GetGuid(),Assert.Single(selected.GetProperty("results").GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Filters_and_pagination_share_stored_decisions_and_never_hide_pending_items()
    {
        using var s=await RecoveryScenario.Create(root);var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var rows=await db.DiscoveryExecutionObservations.OrderBy(x=>x.ItemIndex).ToArrayAsync();
            foreach(var row in rows.Take(3)){
                var node=System.Text.Json.Nodes.JsonNode.Parse(row.PayloadJson)!;
                node["processing"]=System.Text.Json.JsonSerializer.SerializeToNode(new {review=template with {
                    ContentValidity=template.ContentValidity with {Status=row.ItemIndex==2?"INVALID_RECRUITMENT":"VALID_RECRUITMENT"},
                    TargetMatching=template.TargetMatching with {Status=row.ItemIndex==0?"TARGET_MATCH":"NOT_MATCHED"}},vacancies=System.Array.Empty<object>()},DiscoveryReviewRules.Json);
                row.PayloadJson=node.ToJsonString();
            }
            await db.SaveChangesAsync();
        });
        var before=await s.Snapshot();
        async Task<JsonElement> Get(string query)=>await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true&"+query));
        var all=await Get("pageSize=2");Assert.Equal(7,all.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(2,all.GetProperty("counts").GetProperty("valid").GetInt32());Assert.Equal(4,all.GetProperty("counts").GetProperty("unknown").GetInt32());
        var match=await Get("targetStatus=TARGET_MATCH");Assert.Equal(1,match.GetProperty("results").GetProperty("total").GetInt32());
        var invalid=await Get("contentStatus=INVALID_RECRUITMENT");Assert.Equal(1,invalid.GetProperty("results").GetProperty("total").GetInt32());
        var pending=await Get("contentStatus=NEEDS_REVIEW");Assert.Equal(4,pending.GetProperty("results").GetProperty("total").GetInt32());
        var pages=new List<string>();for(var page=1;page<=4;page++){
            var result=await Get($"page={page}&pageSize=2");pages.AddRange(result.GetProperty("results").GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("itemIndex").GetInt32().ToString()));
        }
        Assert.Equal(7,pages.Distinct().Count());Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Five_candidates_and_seven_INGESTING_observations_have_one_read_only_count_scope()
    {
        using var s=await RecoveryScenario.Create(root);
        var item=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            foreach(var i in Enumerable.Range(0,5))db.DiscoveryCandidates.Add(new(){Id=Guid.NewGuid(),RunId=$"older-{i}",Query="Original query",PageNumber=1,ItemIndex=0,
                ContentStatus="NEEDS_REVIEW",TargetStatus="INSUFFICIENT_DATA",Payload=JsonSerializer.Serialize(item,DiscoveryReviewRules.Json),PayloadHash=new string('a',64),IngestedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();
        });
        var before=await s.Snapshot();
        var data=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true&pageSize=5"));
        Assert.Equal(12,data.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(5,data.GetProperty("results").GetProperty("items").GetArrayLength());
        var selected=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+$"?includeObservations=true&executionId={s.Id}&itemIndex=6&pageSize=1"));
        var row=Assert.Single(selected.GetProperty("results").GetProperty("items").EnumerateArray());
        Assert.Equal("EXECUTION_OBSERVATION",row.GetProperty("origin").GetString());Assert.Equal(6,row.GetProperty("itemIndex").GetInt32());
        Assert.Equal(JsonValueKind.Null,row.GetProperty("review").ValueKind);Assert.Equal(JsonValueKind.Null,row.GetProperty("humanReview").ValueKind);
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Unified_projection_still_requires_existing_Admin_authentication()
    {
        using var s=await RecoveryScenario.Create(root,1);using var anonymous=s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true")).StatusCode);
        s.AssertOffline();
    }
}
