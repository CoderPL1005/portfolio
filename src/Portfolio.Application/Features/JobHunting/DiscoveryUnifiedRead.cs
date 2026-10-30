using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

// Read projection only: no candidate ID, query or classifier output is invented.
public sealed record DiscoveryObservationRead(string Origin,Guid ExecutionId,int ItemIndex,string? WorkflowRunId,
    DateTimeOffset CollectedAt,string ExecutionState,JsonElement Source,DiscoveryReviewItem? Review,JsonElement[] Vacancies,
    string SemanticState,string? ExtractionError,string[] SourceWarnings,Guid? CandidateId=null,HumanReviewSummary? HumanReview=null,Guid[]? LinkedCandidateIds=null);
public sealed record DiscoveryOpportunityCounts(int Total,int Valid,int Invalid,int NeedsReview,int TargetMatch,int IdentityWarnings,int DuplicateItems,int Reviewed,int Unknown);
public sealed record DiscoveryOpportunityList(PagedResult<object> Results,DiscoveryOpportunityCounts Counts);
public sealed record GetDiscoveryOpportunitiesQuery(GetDiscoveryReviewQuery Filters,Guid? ExecutionId=null,int? ItemIndex=null):IRequest<DiscoveryOpportunityList>;
public sealed class GetDiscoveryOpportunitiesValidator:IRequestValidator<GetDiscoveryOpportunitiesQuery>
{
    public async Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetDiscoveryOpportunitiesQuery r,CancellationToken ct=default)
    {
        var failures=(await new GetDiscoveryReviewValidator().ValidateAsync(r.Filters,ct)).ToList();
        if(r.ExecutionId==Guid.Empty||r.ItemIndex is <0 or >=200||r.ItemIndex.HasValue&&!r.ExecutionId.HasValue)failures.Add(new("source","Invalid observation coordinates."));
        return failures;
    }
}
public sealed class GetDiscoveryOpportunitiesHandler(IApplicationDbContext db):IRequestHandler<GetDiscoveryOpportunitiesQuery,DiscoveryOpportunityList>
{
    private sealed record Row(object Value,string RunId,DateTimeOffset At,string? Content,string? Target,string? Group,bool Identity,
        HumanReviewSummary? Human,DiscoveryObservationRead? Observation);
    private static string? Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
    private static string? Group(string? raw)=>raw is null?null:DiscoveryReviewRules.Hash(raw.Normalize().Replace("\r\n","\n").Replace('\r','\n').Trim());
    private static bool Reviewed(Row row)=>row.Human is {Status:not "UNREVIEWED"};
    private static bool SameDate(DateTimeOffset? date,JsonElement source)
    {
        var text=Text(source,"publishedAt");
        return text is null?date is null:DateTimeOffset.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var parsed)&&date==parsed;
    }
    private static DiscoveryObservationRead Read(DiscoveryExecution e,DiscoveryExecutionObservation o,DiscoveryExecutionVacancy[] vacancies)
    {
        var warnings=new List<string>();JsonElement payload;
        try{using var document=JsonDocument.Parse(o.PayloadJson);payload=document.RootElement.Clone();}
        catch(JsonException){payload=JsonSerializer.SerializeToElement(new{});warnings.Add("PERSISTED_PAYLOAD_UNREADABLE");}
        DiscoveryReviewItem? review=null;var mapped=System.Array.Empty<JsonElement>();string? error=null;
        if(payload.ValueKind==JsonValueKind.Object&&payload.TryGetProperty("processing",out var p)&&p.ValueKind==JsonValueKind.Object){
            error=Text(p,"extractionError");
            if(p.TryGetProperty("review",out var r)&&r.ValueKind==JsonValueKind.Object){
                try{review=r.Deserialize<DiscoveryReviewItem>(DiscoveryReviewRules.Json);}
                catch(JsonException){warnings.Add("PERSISTED_REVIEW_UNREADABLE");}
                if(review is not null&&(review.ContentValidity is null||review.TargetMatching is null||
                    !DiscoveryReviewRules.ContentStatuses.Contains(review.ContentValidity.Status)||!DiscoveryReviewRules.TargetStatuses.Contains(review.TargetMatching.Status)||
                    review.MissingFields is null||review.SourceWarnings is null||review.Extracted.ValueKind!=JsonValueKind.Object)){
                    review=null;warnings.Add("PERSISTED_REVIEW_UNREADABLE");
                }
            }
            if(p.TryGetProperty("vacancies",out var v)&&v.ValueKind==JsonValueKind.Array)mapped=v.EnumerateArray().Select(x=>x.Clone()).ToArray();
        }
        if(mapped.Length==0)mapped=vacancies.Select(v=>JsonSerializer.SerializeToElement(new{fields=JsonSerializer.Deserialize<JsonElement>(v.FieldsJson),missingFields=JsonSerializer.Deserialize<string[]>(v.MissingFieldsJson),v.Status},DiscoveryReviewRules.Json)).ToArray();
        if(Text(payload,"outcome")=="POST_ID_URL_MISMATCH")warnings.Add("POST_ID_URL_MISMATCH");
        var source=JsonSerializer.SerializeToElement(new{itemIndex=o.ItemIndex,providerPostId=Text(payload,"providerPostId"),sourceUrl=Text(payload,"sourceUrl"),
            providerSourceUrl=Text(payload,"providerSourceUrl"),rawContent=Text(payload,"rawContent"),contentHash=Text(payload,"contentHash"),publishedAt=Text(payload,"publishedAt"),outcome=Text(payload,"outcome")},DiscoveryReviewRules.Json);
        return new("EXECUTION_OBSERVATION",e.Id,o.ItemIndex,e.WorkflowRunId,e.CreatedAt,e.State,source,review,mapped,o.SemanticState,error,warnings.ToArray());
    }
    private static bool SameObservation(DiscoveryReviewDetail candidate,DiscoveryObservationRead observation)
    {
        if(candidate.Recovery is {} recovery)return recovery.ExecutionId==observation.ExecutionId&&recovery.ItemIndex==observation.ItemIndex&&
            recovery.RawContent==Text(observation.Source,"rawContent")&&recovery.ContentHash==Text(observation.Source,"contentHash")&&
            recovery.ProviderPostId==Text(observation.Source,"providerPostId")&&recovery.SourceUrl==Text(observation.Source,"sourceUrl")&&
            recovery.ProviderSourceUrl==Text(observation.Source,"providerSourceUrl")&&SameDate(recovery.PublishedAt,observation.Source);
        if(candidate.Item is not {} item||candidate.PageNumber!=1||item.ItemIndex!=observation.ItemIndex||
            candidate.RunId!=observation.ExecutionId.ToString()&&candidate.RunId!=observation.WorkflowRunId)return false;
        return item.RawContent==Text(observation.Source,"rawContent")&&item.ContentHash==Text(observation.Source,"contentHash")&&
            item.ProviderPostId==Text(observation.Source,"providerPostId")&&item.SourceUrl==Text(observation.Source,"sourceUrl")&&SameDate(item.PublishedAt,observation.Source);
    }
    public async Task<DiscoveryOpportunityList> HandleAsync(GetDiscoveryOpportunitiesQuery request,CancellationToken ct=default)
    {
        // Stored JSON decisions are projected, never reclassified. This initial
        // read model materializes the two collections so dedup, filters, totals
        // and pagination share exactly one population (no page-local union).
        var candidates=await db.DiscoveryCandidates.AsNoTracking().ToArrayAsync(ct);
        var reviews=await db.DiscoveryCandidateReviews.AsNoTracking().ToDictionaryAsync(x=>x.CandidateId,ct);
        var executions=await db.DiscoveryExecutions.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var observations=await db.DiscoveryExecutionObservations.AsNoTracking().OrderBy(x=>x.ExecutionId).ThenBy(x=>x.ItemIndex).ToArrayAsync(ct);
        var vacancies=await db.DiscoveryExecutionVacancies.AsNoTracking().ToArrayAsync(ct);
        var projected=observations.Where(o=>executions.ContainsKey(o.ExecutionId)).Select(o=>Read(executions[o.ExecutionId],o,vacancies.Where(v=>v.ObservationId==o.Id).OrderBy(v=>v.VacancyIndex).ToArray())).ToArray();
        var used=new HashSet<(Guid,int)>();var rows=new List<Row>();
        foreach(var c in candidates.OrderByDescending(c=>reviews.TryGetValue(c.Id,out var h)&&h.Status!="UNREVIEWED")
            .ThenByDescending(c=>reviews.GetValueOrDefault(c.Id)?.ReviewedAt).ThenBy(c=>c.CandidateOrigin!="CLASSIFIER").ThenByDescending(c=>c.IngestedAt).ThenBy(c=>c.Id)){
            var detail=DiscoveryReviewRules.Detail(c,0,reviews.GetValueOrDefault(c.Id));
            var matches=projected.Where(o=>SameObservation(detail,o)).ToArray();
            // Ambiguous provenance is not a license to merge unrelated records.
            var observation=matches.Length==1?matches[0] with {CandidateId=c.Id,HumanReview=detail.HumanReview,LinkedCandidateIds=[c.Id]}:null;
            if(observation is not null&&used.Contains((observation.ExecutionId,observation.ItemIndex))){
                var index=rows.FindIndex(x=>x.Observation?.ExecutionId==observation.ExecutionId&&x.Observation.ItemIndex==observation.ItemIndex);
                var prior=rows[index];var linked=prior.Observation! with {LinkedCandidateIds=[..prior.Observation!.LinkedCandidateIds!,c.Id],
                    SourceWarnings=[..prior.Observation.SourceWarnings.Where(x=>x!="MULTIPLE_CANDIDATE_RECORDS"),"MULTIPLE_CANDIDATE_RECORDS"]};
                rows[index]=prior with {Value=((DiscoveryReviewDetail)prior.Value) with {Observation=linked},Observation=linked};
                continue; // Display one observation; retain every real review/ID in storage and read provenance.
            }
            if(observation is not null)used.Add((observation.ExecutionId,observation.ItemIndex));
            detail=detail with {Observation=observation};
            rows.Add(new(detail,c.RunId,c.IngestedAt,detail.Item?.ContentValidity.Status,detail.Item?.TargetMatching.Status,
                Group(detail.Item?.RawContent??detail.Recovery?.RawContent),c.IdentityWarning||observation?.SourceWarnings.Contains("POST_ID_URL_MISMATCH")==true,detail.HumanReview,observation));
        }
        foreach(var o in projected.Where(o=>!used.Contains((o.ExecutionId,o.ItemIndex))))rows.Add(new(o,o.ExecutionId.ToString(),o.CollectedAt,
            o.Review?.ContentValidity?.Status,o.Review?.TargetMatching?.Status,Group(Text(o.Source,"rawContent")),o.SourceWarnings.Contains("POST_ID_URL_MISMATCH"),null,o));
        var groups=rows.Where(x=>x.Group is not null).GroupBy(x=>x.Group!).ToDictionary(g=>g.Key,g=>g.Count());
        var f=request.Filters;IEnumerable<Row> filtered=rows;
        if(f.RunId is not null)filtered=filtered.Where(x=>x.RunId==f.RunId||x.Observation?.ExecutionId.ToString()==f.RunId||x.Observation?.WorkflowRunId==f.RunId);
        if(f.CollectedFrom.HasValue)filtered=filtered.Where(x=>x.At>=f.CollectedFrom.Value);
        if(f.CollectedBefore.HasValue)filtered=filtered.Where(x=>x.At<f.CollectedBefore.Value);
        if(f.ContentStatus is not null)filtered=filtered.Where(x=>x.Content==f.ContentStatus||f.ContentStatus=="NEEDS_REVIEW"&&x.Content is null);
        if(f.TargetStatus is not null)filtered=filtered.Where(x=>x.Target==f.TargetStatus);
        if(f.IdentityWarning.HasValue)filtered=filtered.Where(x=>x.Identity==f.IdentityWarning);
        if(f.DuplicatesOnly==true)filtered=filtered.Where(x=>x.Group is not null&&groups[x.Group]>1);
        if(f.ReviewState=="REVIEWED")filtered=filtered.Where(Reviewed);
        else if(f.ReviewState=="UNREVIEWED")filtered=filtered.Where(x=>!Reviewed(x));
        else if(f.ReviewState is not null)filtered=filtered.Where(x=>x.Human?.Status==f.ReviewState);
        if(request.ExecutionId.HasValue)filtered=filtered.Where(x=>x.Observation?.ExecutionId==request.ExecutionId);
        if(request.ItemIndex.HasValue)filtered=filtered.Where(x=>x.Observation?.ItemIndex==request.ItemIndex);
        var selected=filtered.ToArray();
        var counts=new DiscoveryOpportunityCounts(selected.Length,selected.Count(x=>x.Content=="VALID_RECRUITMENT"),selected.Count(x=>x.Content=="INVALID_RECRUITMENT"),
            selected.Count(x=>x.Content is null or "NEEDS_REVIEW"),selected.Count(x=>x.Target=="TARGET_MATCH"),
            selected.Count(x=>x.Identity),selected.Count(x=>x.Group is not null&&groups[x.Group]>1),selected.Count(Reviewed),selected.Count(x=>x.Content is null));
        var page=selected.OrderByDescending(x=>x.Target=="TARGET_MATCH"&&!Reviewed(x)).ThenByDescending(x=>x.At).ThenBy(x=>x.RunId,StringComparer.Ordinal)
            .ThenBy(x=>(x.Value as DiscoveryReviewDetail)?.PageNumber??0)
            .ThenBy(x=>x.Observation?.ItemIndex??(x.Value as DiscoveryReviewDetail)?.Item?.ItemIndex??(x.Value as DiscoveryReviewDetail)?.Recovery?.ItemIndex??0)
            .ThenBy(x=>(x.Value as DiscoveryReviewDetail)?.Id??x.Observation!.ExecutionId)
            .Skip((f.Page-1)*f.PageSize).Take(f.PageSize).Select(x=>x.Value is DiscoveryReviewDetail c?c with {DuplicateCount=x.Group is null?0:groups[x.Group]}:x.Value).ToArray();
        return new(new(page,f.Page,f.PageSize,selected.Length),counts);
    }
}
