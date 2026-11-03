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
                ContentStatus="NEEDS_REVIEW",TargetStatus="INSUFFICIENT_DATA",Payload=JsonSerializer.Serialize(item,DiscoveryReviewRules.Json),PayloadHash=new string('a',64),IngestedAt=new DateTimeOffset(2026,10,28,0,0,0,TimeSpan.Zero)});
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
    [Fact] public async Task Newest_collection_session_comes_first_and_date_filter_scopes_counts_and_stable_pagination()
    {
        using var s=await RecoveryScenario.Create(root,5);
        var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        var slot1600Id=s.Id;
        var slot1900Id=Guid.Parse("88888888-8888-4888-8888-888888888888");
        var yesterdayId=Guid.Parse("77777777-7777-4777-8777-777777777777");
        var slot1600At=new DateTimeOffset(2026,11,2,9,0,0,TimeSpan.Zero); // 16:00 Vietnam
        var slot1900At=new DateTimeOffset(2026,11,2,12,0,0,TimeSpan.Zero); // 19:00 Vietnam
        var yesterdayEndAt=new DateTimeOffset(2026,11,1,16,59,59,TimeSpan.Zero); // 23:59:59 01/11 Vietnam
        await s.Read(async db=>{
            var exec1600=await db.DiscoveryExecutions.SingleAsync(x=>x.Id==slot1600Id);
            exec1600.CreatedAt=slot1600At;exec1600.UpdatedAt=slot1600At;
            var obs1600=await db.DiscoveryExecutionObservations.Where(x=>x.ExecutionId==slot1600Id).OrderBy(x=>x.ItemIndex).ToArrayAsync();
            foreach(var row in obs1600){
                var node=System.Text.Json.Nodes.JsonNode.Parse(row.PayloadJson)!;
                node["processing"]=JsonSerializer.SerializeToNode(new {review=template with {
                    ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                    TargetMatching=template.TargetMatching with {Status="TARGET_MATCH"}},vacancies=System.Array.Empty<object>()},DiscoveryReviewRules.Json);
                row.PayloadJson=node.ToJsonString();
            }
            db.DiscoveryExecutions.Add(new(){Id=slot1900Id,ClientRequestId=Guid.NewGuid(),CreatedByAdminId=AuthApiFactory.AdminId,
                State="PARTIAL",WorkflowRunId="10002",Version=1,CreatedAt=slot1900At,UpdatedAt=slot1900At});
            foreach(var index in Enumerable.Range(0,5)){
                var raw=$"Slot 19:00 employer {index} hiring Backend Developer in Hanoi.";
                var hash=DiscoveryReviewRules.Hash(raw);
                var payload=JsonSerializer.SerializeToNode(new {itemIndex=index,providerPostId=$"1900{index}",sourceUrl=$"https://www.facebook.com/fixture/posts/1900{index}",
                    providerSourceUrl=$"https://www.facebook.com/fixture/posts/1900{index}",rawContent=raw,contentHash=hash,publishedAt=(string?)null,outcome="READY_FOR_EXTRACTION",
                    processing=new {review=template with {
                        ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                        TargetMatching=template.TargetMatching with {Status="NOT_MATCHED"}},vacancies=System.Array.Empty<object>()}},DiscoveryReviewRules.Json)!.ToJsonString();
                db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=slot1900Id,ItemIndex=index,
                    SemanticState="NOT_REQUESTED",ContentHash=hash,PayloadHash=DiscoveryReviewRules.Hash(payload),PayloadJson=payload});
            }
            db.DiscoveryExecutions.Add(new(){Id=yesterdayId,ClientRequestId=Guid.NewGuid(),CreatedByAdminId=AuthApiFactory.AdminId,
                State="PARTIAL",WorkflowRunId="10000",Version=1,CreatedAt=yesterdayEndAt,UpdatedAt=yesterdayEndAt});
            var oldRaw="Yesterday employer hiring Backend Developer.";
            var oldHash=DiscoveryReviewRules.Hash(oldRaw);
            var oldPayload=JsonSerializer.SerializeToNode(new {itemIndex=0,providerPostId="7700",sourceUrl="https://www.facebook.com/fixture/posts/7700",
                providerSourceUrl="https://www.facebook.com/fixture/posts/7700",rawContent=oldRaw,contentHash=oldHash,publishedAt=(string?)null,outcome="READY_FOR_EXTRACTION"},DiscoveryReviewRules.Json)!.ToJsonString();
            db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=yesterdayId,ItemIndex=0,
                SemanticState="NOT_REQUESTED",ContentHash=oldHash,PayloadHash=DiscoveryReviewRules.Hash(oldPayload),PayloadJson=oldPayload});
            await db.SaveChangesAsync();
        });
        var before=await s.Snapshot();
        var todayUrl=RecoveryScenario.ReviewPath+"?includeObservations=true&collectedFrom=2026-11-01T17:00:00.000Z&collectedBefore=2026-11-02T17:00:00.000Z&pageSize=20";
        var today=await RecoveryScenario.Data(await s.Client.GetAsync(todayUrl));
        Assert.Equal(10,today.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(5,today.GetProperty("counts").GetProperty("targetMatch").GetInt32());
        var items=today.GetProperty("results").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(10,items.Length);
        Assert.All(items.Take(5),x=>Assert.Equal(slot1900Id,x.GetProperty("executionId").GetGuid()));
        Assert.Equal(new[]{0,1,2,3,4},items.Take(5).Select(x=>x.GetProperty("itemIndex").GetInt32()).ToArray());
        Assert.All(items.Skip(5),x=>Assert.Equal(slot1600Id,x.GetProperty("executionId").GetGuid()));
        Assert.Equal(new[]{0,1,2,3,4},items.Skip(5).Select(x=>x.GetProperty("itemIndex").GetInt32()).ToArray());
        var pagedKeys=new List<string>();
        for(var page=1;page<=4;page++){
            var pageData=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+$"?includeObservations=true&collectedFrom=2026-11-01T17:00:00.000Z&collectedBefore=2026-11-02T17:00:00.000Z&page={page}&pageSize=3"));
            pagedKeys.AddRange(pageData.GetProperty("results").GetProperty("items").EnumerateArray().Select(x=>$"{x.GetProperty("executionId").GetGuid()}:{x.GetProperty("itemIndex").GetInt32()}"));
        }
        Assert.Equal(10,pagedKeys.Count);
        Assert.Equal(10,pagedKeys.Distinct().Count());
        Assert.Equal(items.Select(x=>$"{x.GetProperty("executionId").GetGuid()}:{x.GetProperty("itemIndex").GetInt32()}"),pagedKeys);
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Candidate_linked_to_observation_uses_observation_collection_time_and_does_not_jump_days_when_reviewed_later()
    {
        using var s=await RecoveryScenario.Create(root,3);
        var lateIngestedAt=new DateTimeOffset(2026,11,2,10,0,0,TimeSpan.Zero);
        var result=await s.NormalResult();
        var item=result.ReviewBundle!.Items[0];
        var payload=JsonSerializer.Serialize(item,DiscoveryReviewRules.Json);
        await s.Read(async db=>{
            var c=new DiscoveryCandidate{Id=Guid.NewGuid(),RunId=s.Id.ToString(),Query="Synthetic original query",PageNumber=1,ItemIndex=0,
                ContentStatus="VALID_RECRUITMENT",TargetStatus="TARGET_MATCH",ContentGroupHash=item.ContentGroupHash,
                Payload=payload,PayloadHash=DiscoveryReviewRules.Hash("Synthetic original query\n"+payload),IngestedAt=lateIngestedAt};
            db.DiscoveryCandidates.Add(c);
            db.DiscoveryCandidateReviews.Add(new(){CandidateId=c.Id,Version=1,Status="CONFIRMED",
                ContentStatus="VALID_RECRUITMENT",TargetStatus="TARGET_MATCH",Notes="Late review does not change collection date",
                ReviewedAt=lateIngestedAt.AddHours(1),ReviewedByAdminId=AuthApiFactory.AdminId});
            await db.SaveChangesAsync();
        });
        var before=await s.Snapshot();
        var nov2=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true&collectedFrom=2026-11-01T17:00:00.000Z&collectedBefore=2026-11-02T17:00:00.000Z"));
        Assert.Equal(0,nov2.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Empty(nov2.GetProperty("results").GetProperty("items").EnumerateArray());
        var oct29=await RecoveryScenario.Data(await s.Client.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true&collectedFrom=2026-10-28T17:00:00.000Z&collectedBefore=2026-10-29T17:00:00.000Z"));
        Assert.Equal(3,oct29.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1,oct29.GetProperty("counts").GetProperty("reviewed").GetInt32());
        var oct29Items=oct29.GetProperty("results").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3,oct29Items.Length);
        Assert.Equal("CLASSIFIER",oct29Items[0].GetProperty("origin").GetString());
        Assert.Equal("CONFIRMED",oct29Items[0].GetProperty("humanReview").GetProperty("status").GetString());
        Assert.Equal(1,oct29Items[1].GetProperty("itemIndex").GetInt32());
        Assert.Equal(2,oct29Items[2].GetProperty("itemIndex").GetInt32());
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
    [Fact] public async Task Unified_projection_still_requires_existing_Admin_authentication()
    {
        using var s=await RecoveryScenario.Create(root,1);using var anonymous=s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(RecoveryScenario.ReviewPath+"?includeObservations=true")).StatusCode);
        s.AssertOffline();
    }

    [Fact] public async Task Cross_execution_dedup_tracks_occurrences_content_updates_reposts_date_provenance_and_preserves_raw_and_human_review()
    {
        using var s=await RecoveryScenario.Create(root,1);
        var template=(await s.NormalResult()).ReviewBundle!.Items[0];
        var exec1Id=s.Id;
        var exec2Id=Guid.Parse("11111111-2222-4333-8444-555555555555");
        var exec3Id=Guid.Parse("22222222-3333-4444-8555-666666666666");
        var rawPostId=Guid.NewGuid();
        var t1=new DateTimeOffset(2026,11,2,1,0,0,TimeSpan.Zero); // 08:00 ICT Nov 2
        var t2=new DateTimeOffset(2026,11,2,4,0,0,TimeSpan.Zero); // 11:00 ICT Nov 2
        var t3=new DateTimeOffset(2026,11,2,7,0,0,TimeSpan.Zero); // 14:00 ICT Nov 2
        const string originalText="[T3SKY] Tuyển dụng Backend Intern (.NET) tại Hà Nội #dotnet #csharp #backend";
        const string updatedText="[T3SKY] Tuyển dụng Backend Intern (.NET) tại Hà Nội (Updated deadline 30/11) #dotnet #csharp #backend";
        var hash1=DiscoveryReviewRules.Hash(originalText);
        var hash2=DiscoveryReviewRules.Hash(updatedText);

        await s.Read(async db=>{
            db.DiscoveryExecutionObservations.RemoveRange(db.DiscoveryExecutionObservations.Where(x=>x.ExecutionId==exec1Id));
            db.RawJobPostings.Add(new(){Id=rawPostId,RawContent=originalText,ContentHash=hash1,Metadata=JsonDocument.Parse("{}")});
            var exec1=await db.DiscoveryExecutions.SingleAsync(x=>x.Id==exec1Id);
            exec1.CreatedAt=t1;exec1.UpdatedAt=t1;
            var payload1=JsonSerializer.SerializeToNode(new {
                itemIndex=0,providerPostId="post_t3sky_1",sourceUrl="https://www.facebook.com/fixture/posts/post_t3sky_1",
                providerSourceUrl="https://www.facebook.com/fixture/posts/post_t3sky_1",rawContent=originalText,contentHash=hash1,contentGroupHash=hash1,
                publishedAt="2026-11-02T00:30:00Z",publishedAtProvenance="VERIFIED_SUPPORTED_TIMESTAMP",windowMatchStatus="VERIFIED_IN_WINDOW",
                outcome="READY_FOR_EXTRACTION",
                processing=new {review=template with {
                    ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                    TargetMatching=template.TargetMatching with {Status="TARGET_MATCH"}},vacancies=System.Array.Empty<object>()}
            },DiscoveryReviewRules.Json)!.ToJsonString();
            db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=exec1Id,ItemIndex=0,
                SemanticState="NOT_REQUESTED",ContentHash=hash1,PayloadHash=DiscoveryReviewRules.Hash(payload1),PayloadJson=payload1});

            // Add reviewed candidate for exec1 obs1
            var candidateItem=template with {
                ItemIndex=0,ProviderPostId="post_t3sky_1",SourceUrl="https://www.facebook.com/fixture/posts/post_t3sky_1",
                RawContent=originalText,ContentHash=hash1,ContentGroupHash=hash1,PublishedAt=DateTimeOffset.Parse("2026-11-02T00:30:00Z"),
                ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                TargetMatching=template.TargetMatching with {Status="TARGET_MATCH"}
            };
            var candPayload=JsonSerializer.Serialize(candidateItem,DiscoveryReviewRules.Json);
            var cand=new DiscoveryCandidate{Id=Guid.NewGuid(),RunId=exec1Id.ToString(),Query=".NET Backend Intern Hanoi",PageNumber=1,ItemIndex=0,
                ContentStatus="VALID_RECRUITMENT",TargetStatus="TARGET_MATCH",ContentGroupHash=hash1,Payload=candPayload,
                PayloadHash=DiscoveryReviewRules.Hash(candPayload),IngestedAt=t1};
            db.DiscoveryCandidates.Add(cand);
            db.DiscoveryCandidateReviews.Add(new(){CandidateId=cand.Id,Version=1,Status="CONFIRMED",
                ContentStatus="VALID_RECRUITMENT",TargetStatus="TARGET_MATCH",Notes="Verified T3SKY Intern post",
                ReviewedAt=t1.AddMinutes(15),ReviewedByAdminId=AuthApiFactory.AdminId});

            // Exec 2: Same post repeated with updated content + a second post with exact repost of originalText under different post_id and unknown date
            db.DiscoveryExecutions.Add(new(){Id=exec2Id,ClientRequestId=Guid.NewGuid(),CreatedByAdminId=AuthApiFactory.AdminId,
                State="PARTIAL",WorkflowRunId="20002",Version=1,CreatedAt=t2,UpdatedAt=t2});
            var payload2A=JsonSerializer.SerializeToNode(new {
                itemIndex=0,providerPostId="post_t3sky_1",sourceUrl="https://www.facebook.com/fixture/posts/post_t3sky_1",
                providerSourceUrl="https://www.facebook.com/fixture/posts/post_t3sky_1",rawContent=updatedText,contentHash=hash2,contentGroupHash=hash2,
                publishedAt="2026-11-02T00:30:00Z",publishedAtProvenance="VERIFIED_SUPPORTED_TIMESTAMP",windowMatchStatus="VERIFIED_IN_WINDOW",
                outcome="READY_FOR_EXTRACTION",
                processing=new {review=template with {
                    ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                    TargetMatching=template.TargetMatching with {Status="TARGET_MATCH"}},vacancies=System.Array.Empty<object>()}
            },DiscoveryReviewRules.Json)!.ToJsonString();
            db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=exec2Id,ItemIndex=0,
                SemanticState="NOT_REQUESTED",ContentHash=hash2,PayloadHash=DiscoveryReviewRules.Hash(payload2A),PayloadJson=payload2A});

            var payload2B=JsonSerializer.SerializeToNode(new {
                itemIndex=1,providerPostId="post_repost_99",sourceUrl="https://www.facebook.com/fixture/posts/post_repost_99",
                providerSourceUrl="https://www.facebook.com/fixture/posts/post_repost_99",rawContent=originalText,contentHash=hash1,contentGroupHash=hash1,
                publishedAt=(string?)null,publishedAtProvenance="UNAVAILABLE_OR_UNVERIFIED",windowMatchStatus="UNKNOWN_DATE",
                outcome="READY_FOR_EXTRACTION",
                processing=new {review=template with {
                    ContentValidity=template.ContentValidity with {Status="VALID_RECRUITMENT"},
                    TargetMatching=template.TargetMatching with {Status="TARGET_MATCH"}},vacancies=System.Array.Empty<object>()}
            },DiscoveryReviewRules.Json)!.ToJsonString();
            db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=exec2Id,ItemIndex=1,
                SemanticState="NOT_REQUESTED",ContentHash=hash1,PayloadHash=DiscoveryReviewRules.Hash(payload2B),PayloadJson=payload2B});

            // Exec 3: Same post_repost_99 repeated unchanged
            db.DiscoveryExecutions.Add(new(){Id=exec3Id,ClientRequestId=Guid.NewGuid(),CreatedByAdminId=AuthApiFactory.AdminId,
                State="PARTIAL",WorkflowRunId="20003",Version=1,CreatedAt=t3,UpdatedAt=t3});
            db.DiscoveryExecutionObservations.Add(new(){Id=Guid.NewGuid(),ExecutionId=exec3Id,ItemIndex=0,
                SemanticState="NOT_REQUESTED",ContentHash=hash1,PayloadHash=DiscoveryReviewRules.Hash(payload2B),PayloadJson=payload2B});

            await db.SaveChangesAsync();
        });

        var before=await s.Snapshot();
        var baseQuery=RecoveryScenario.ReviewPath+"?includeObservations=true&collectedFrom=2026-11-01T17:00:00.000Z&collectedBefore=2026-11-02T17:00:00.000Z";

        // 1. Default (collapseDuplicates=false): all 4 observations visible, with occurrenceCount and duplicateStatus populated
        var allData=await RecoveryScenario.Data(await s.Client.GetAsync(baseQuery));
        var counts=allData.GetProperty("counts");
        Assert.Equal(4,counts.GetProperty("total").GetInt32());
        Assert.Equal(4,counts.GetProperty("totalObservations").GetInt32());
        Assert.Equal(2,counts.GetProperty("uniqueSourcePosts").GetInt32());
        Assert.Equal(2,counts.GetProperty("newUniquePostsToday").GetInt32());
        Assert.Equal(2,counts.GetProperty("repeatedPosts").GetInt32());
        Assert.Equal(1,counts.GetProperty("unknownDatePosts").GetInt32());
        Assert.Equal(2,counts.GetProperty("newRelevantVacancies").GetInt32());
        Assert.Equal(2,counts.GetProperty("repeatedRelevantVacancies").GetInt32());
        Assert.Equal(1,counts.GetProperty("contentUpdatedPosts").GetInt32());
        Assert.Equal(1,counts.GetProperty("exactContentReposts").GetInt32());

        // 2. Collapsed mode (collapseDuplicates=true): 2 unique posts returned, preserving Human Review on post_t3sky_1 and listing all occurrences
        var collapsedData=await RecoveryScenario.Data(await s.Client.GetAsync(baseQuery+"&collapseDuplicates=true"));
        Assert.Equal(2,collapsedData.GetProperty("results").GetProperty("total").GetInt32());
        var collapsedItems=collapsedData.GetProperty("results").GetProperty("items").EnumerateArray().ToArray();
        var t3skyCard=collapsedItems.Single(x=>x.TryGetProperty("humanReview",out var hr)&&hr.ValueKind==JsonValueKind.Object);
        Assert.Equal("CONFIRMED",t3skyCard.GetProperty("humanReview").GetProperty("status").GetString());
        Assert.Equal("CONTENT_UPDATED",t3skyCard.GetProperty("duplicateStatus").GetString());
        Assert.Equal(2,t3skyCard.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal(2,t3skyCard.GetProperty("occurrences").GetArrayLength());

        var repostCard=collapsedItems.Single(x=>x.GetProperty("origin").GetString()=="EXECUTION_OBSERVATION");
        Assert.Equal("EXACT_CONTENT_REPOST",repostCard.GetProperty("duplicateStatus").GetString());
        Assert.True(repostCard.GetProperty("occurrenceCount").GetInt32()>=2);

        // 3. Date provenance filter separates VERIFIED_DATE from UNKNOWN_DATE
        var verifiedOnly=await RecoveryScenario.Data(await s.Client.GetAsync(baseQuery+"&dateProvenance=VERIFIED_DATE"));
        Assert.Equal(2,verifiedOnly.GetProperty("results").GetProperty("total").GetInt32());
        var unknownOnly=await RecoveryScenario.Data(await s.Client.GetAsync(baseQuery+"&dateProvenance=UNKNOWN_DATE"));
        Assert.Equal(2,unknownOnly.GetProperty("results").GetProperty("total").GetInt32());

        // 4. RawJobPosting.RawContent and historical observations remain 100% immutable
        await s.Read(async db=>{
            var rawPost=await db.RawJobPostings.SingleAsync(x=>x.Id==rawPostId);
            Assert.Equal(originalText,rawPost.RawContent);
            Assert.Equal(4,await db.DiscoveryExecutionObservations.CountAsync());
        });
        Assert.Equal(before,await s.Snapshot());s.AssertOffline();
    }
}
