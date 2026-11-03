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
    string SemanticState,string? ExtractionError,string[] SourceWarnings,Guid? CandidateId=null,HumanReviewSummary? HumanReview=null,Guid[]? LinkedCandidateIds=null,
    string? ProviderPostIdentity=null,int OccurrenceCount=1,string? DuplicateStatus=null,DiscoveryPostOccurrence[]? Occurrences=null,
    string PublishedAtProvenance="UNAVAILABLE_OR_UNVERIFIED",string WindowMatchStatus="UNKNOWN_DATE");
public sealed record DiscoveryOpportunityCounts(int Total,int Valid,int Invalid,int NeedsReview,int TargetMatch,int IdentityWarnings,int DuplicateItems,int Reviewed,int Unknown,
    int TotalObservations=0,int UniqueSourcePosts=0,int NewUniquePostsToday=0,int RepeatedPosts=0,int UnknownDatePosts=0,int NewRelevantVacancies=0,int RepeatedRelevantVacancies=0,int ContentUpdatedPosts=0,int ExactContentReposts=0);
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
    private sealed record Row(object Value,string RunId,DateTimeOffset At,string? Content,string? Target,string? Group,string? ContentHash,
        string? ProviderIdentity,bool Identity,bool HasVerifiedDate,HumanReviewSummary? Human,DiscoveryObservationRead? Observation,
        int OccurrenceCount=1,string? DuplicateStatus=null,DiscoveryPostOccurrence[]? Occurrences=null,DateTimeOffset FirstAt=default);
    private static string? Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
    private static string? Group(string? raw)=>raw is null?null:DiscoveryReviewRules.Hash(raw.Normalize().Replace("\r\n","\n").Replace('\r','\n').Trim());
    private static bool Reviewed(Row row)=>row.Human is {Status:not "UNREVIEWED"};
    private static string? BuildProviderIdentity(string? postId,string? sourceUrl,bool identityConflict)
    {
        if(identityConflict)return null;
        if(!string.IsNullOrWhiteSpace(postId)&&System.Text.RegularExpressions.Regex.IsMatch(postId,"^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200}|[A-Za-z0-9_.-]{1,100})$"))return "fb:id:"+postId;
        if(!string.IsNullOrWhiteSpace(sourceUrl)&&DiscoveryReviewRules.SafeUrl(sourceUrl))return "fb:url:"+sourceUrl;
        return null;
    }
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
        var outcome=Text(payload,"outcome");
        var idMismatch=outcome=="POST_ID_URL_MISMATCH"||review?.SourceWarnings?.Contains("POST_ID_URL_MISMATCH")==true;
        if(idMismatch&&!warnings.Contains("POST_ID_URL_MISMATCH"))warnings.Add("POST_ID_URL_MISMATCH");
        var pubText=Text(payload,"publishedAt");
        var hasValidDate=pubText is not null&&DateTimeOffset.TryParse(pubText,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _);
        var provenance=Text(payload,"publishedAtProvenance")??(hasValidDate?"VERIFIED_SUPPORTED_TIMESTAMP":"UNAVAILABLE_OR_UNVERIFIED");
        var windowStatus=Text(payload,"windowMatchStatus")??(hasValidDate?"VERIFIED_IN_WINDOW":"UNKNOWN_DATE");
        var postId=Text(payload,"providerPostId");var sourceUrl=Text(payload,"sourceUrl");
        var source=JsonSerializer.SerializeToElement(new{itemIndex=o.ItemIndex,providerPostId=postId,sourceUrl,
            providerSourceUrl=Text(payload,"providerSourceUrl"),rawContent=Text(payload,"rawContent"),contentHash=Text(payload,"contentHash"),
            publishedAt=pubText,publishedAtProvenance=provenance,windowMatchStatus=windowStatus,outcome},DiscoveryReviewRules.Json);
        return new("EXECUTION_OBSERVATION",e.Id,o.ItemIndex,e.WorkflowRunId,e.CreatedAt,e.State,source,review,mapped,o.SemanticState,error,warnings.ToArray(),
            ProviderPostIdentity:BuildProviderIdentity(postId,sourceUrl,idMismatch),PublishedAtProvenance:provenance,WindowMatchStatus:windowStatus);
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
            var collectedAt=observation?.CollectedAt??c.IngestedAt;
            var idConflict=c.IdentityWarning||observation?.SourceWarnings.Contains("POST_ID_URL_MISMATCH")==true;
            var postId=detail.Item?.ProviderPostId??detail.Recovery?.ProviderPostId??(observation is not null?Text(observation.Source,"providerPostId"):null);
            var sourceUrl=detail.Item?.SourceUrl??detail.Recovery?.SourceUrl??(observation is not null?Text(observation.Source,"sourceUrl"):null);
            var providerIdentity=BuildProviderIdentity(postId,sourceUrl,idConflict);
            var hasVerifiedDate=observation is not null ? (Text(observation.Source,"publishedAt")!=null&&observation.PublishedAtProvenance=="VERIFIED_SUPPORTED_TIMESTAMP") : (detail.Item?.PublishedAt??detail.Recovery?.PublishedAt).HasValue;
            var rawText=detail.Item?.RawContent??detail.Recovery?.RawContent??(observation is not null?Text(observation.Source,"rawContent"):null);
            var contentHash=detail.Item?.ContentHash??detail.Recovery?.ContentHash??(observation is not null?Text(observation.Source,"contentHash"):null);
            detail=detail with {Observation=observation,ProviderPostIdentity=providerIdentity};
            rows.Add(new(detail,c.RunId,collectedAt,detail.Item?.ContentValidity.Status,detail.Item?.TargetMatching.Status,
                Group(rawText),contentHash,providerIdentity,idConflict,hasVerifiedDate,detail.HumanReview,observation));
        }
        foreach(var o in projected.Where(o=>!used.Contains((o.ExecutionId,o.ItemIndex))))rows.Add(new(o,o.ExecutionId.ToString(),o.CollectedAt,
            o.Review?.ContentValidity?.Status,o.Review?.TargetMatching?.Status,Group(Text(o.Source,"rawContent")),Text(o.Source,"contentHash"),
            o.ProviderPostIdentity,o.SourceWarnings.Contains("POST_ID_URL_MISMATCH"),Text(o.Source,"publishedAt")!=null&&o.PublishedAtProvenance=="VERIFIED_SUPPORTED_TIMESTAMP",null,o));
        var groups=rows.Where(x=>x.Group is not null).GroupBy(x=>x.Group!).ToDictionary(g=>g.Key,g=>g.Count());
        var providerGroups=rows.Where(x=>x.ProviderIdentity is not null).GroupBy(x=>x.ProviderIdentity!,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderBy(r=>r.At).ToArray(),StringComparer.Ordinal);
        var contentGroups=rows.Where(x=>x.Group is not null).GroupBy(x=>x.Group!,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderBy(r=>r.At).ToArray(),StringComparer.Ordinal);
        static DiscoveryPostOccurrence ToOccurrence(Row r,string? baselineGroup)=>new(
            r.Value is DiscoveryReviewDetail d?d.Origin:"EXECUTION_OBSERVATION",
            (r.Value as DiscoveryReviewDetail)?.Id??r.Observation?.CandidateId,
            r.Observation?.ExecutionId,
            r.Observation?.ItemIndex??(r.Value as DiscoveryReviewDetail)?.Item?.ItemIndex??(r.Value as DiscoveryReviewDetail)?.Recovery?.ItemIndex??0,
            r.Observation?.ExecutionId.ToString()??r.RunId,
            r.At,r.ContentHash,r.Group,baselineGroup is not null&&r.Group is not null&&r.Group!=baselineGroup,Reviewed(r));

        var enrichedRows=rows.Select(r=>{
            var related=r.ProviderIdentity is not null&&providerGroups.TryGetValue(r.ProviderIdentity,out var pg)?pg
                :r.Group is not null&&contentGroups.TryGetValue(r.Group,out var cg)?cg:[r];
            var firstAt=related.Min(x=>x.At);
            var baselineGroup=related[0].Group;
            var occurrences=related.OrderByDescending(x=>x.At).Select(x=>ToOccurrence(x,baselineGroup)).ToArray();
            var occCount=related.Length;
            var hasContentUpdate=r.ProviderIdentity is not null&&related.Select(x=>x.Group).Where(g=>g is not null).Distinct(StringComparer.Ordinal).Count()>1;
            var hasExactRepost=r.Group is not null&&contentGroups.TryGetValue(r.Group,out var sameContent)&&
                sameContent.Select(x=>x.ProviderIdentity??$"row:{x.RunId}:{x.At.Ticks}").Distinct(StringComparer.Ordinal).Count()>1;
            var dupStatus=r.Identity?"IDENTITY_CONFLICT":hasContentUpdate?"CONTENT_UPDATED":hasExactRepost?"EXACT_CONTENT_REPOST":occCount>1?"SAME_POST_REPEAT":"UNIQUE";
            object updatedValue=r.Value switch {
                DiscoveryReviewDetail detail=>detail with {
                    DuplicateCount=occCount,
                    OccurrenceCount=occCount,
                    DuplicateStatus=dupStatus,
                    Occurrences=occurrences,
                    Observation=detail.Observation is null?null:detail.Observation with {OccurrenceCount=occCount,DuplicateStatus=dupStatus,Occurrences=occurrences}
                },
                DiscoveryObservationRead obs=>obs with {
                    OccurrenceCount=occCount,
                    DuplicateStatus=dupStatus,
                    Occurrences=occurrences,
                    SourceWarnings=hasContentUpdate&&!obs.SourceWarnings.Contains("CONTENT_UPDATED")?[..obs.SourceWarnings,"CONTENT_UPDATED"]:
                        hasExactRepost&&!obs.SourceWarnings.Contains("EXACT_CONTENT_REPOST")?[..obs.SourceWarnings,"EXACT_CONTENT_REPOST"]:obs.SourceWarnings
                },
                _=>r.Value
            };
            return r with {Value=updatedValue,OccurrenceCount=occCount,DuplicateStatus=dupStatus,Occurrences=occurrences,FirstAt=firstAt};
        }).ToArray();

        var f=request.Filters;IEnumerable<Row> filtered=enrichedRows;
        if(f.RunId is not null)filtered=filtered.Where(x=>x.RunId==f.RunId||x.Observation?.ExecutionId.ToString()==f.RunId||x.Observation?.WorkflowRunId==f.RunId);
        if(f.CollectedFrom.HasValue)filtered=filtered.Where(x=>x.At>=f.CollectedFrom.Value);
        if(f.CollectedBefore.HasValue)filtered=filtered.Where(x=>x.At<f.CollectedBefore.Value);
        if(f.DateProvenance=="VERIFIED_DATE")filtered=filtered.Where(x=>x.HasVerifiedDate);
        else if(f.DateProvenance=="UNKNOWN_DATE")filtered=filtered.Where(x=>!x.HasVerifiedDate);
        if(f.ContentStatus is not null)filtered=filtered.Where(x=>x.Content==f.ContentStatus||f.ContentStatus=="NEEDS_REVIEW"&&x.Content is null);
        if(f.TargetStatus is not null)filtered=filtered.Where(x=>x.Target==f.TargetStatus);
        if(f.IdentityWarning.HasValue)filtered=filtered.Where(x=>x.Identity==f.IdentityWarning);
        if(f.DuplicatesOnly==true)filtered=filtered.Where(x=>x.OccurrenceCount>1||(x.Group is not null&&groups[x.Group]>1));
        if(f.ReviewState=="REVIEWED")filtered=filtered.Where(Reviewed);
        else if(f.ReviewState=="UNREVIEWED")filtered=filtered.Where(x=>!Reviewed(x));
        else if(f.ReviewState is not null)filtered=filtered.Where(x=>x.Human?.Status==f.ReviewState);
        if(request.ExecutionId.HasValue)filtered=filtered.Where(x=>x.Observation?.ExecutionId==request.ExecutionId);
        if(request.ItemIndex.HasValue)filtered=filtered.Where(x=>x.Observation?.ItemIndex==request.ItemIndex);
        var allFiltered=filtered.ToArray();

        static string DedupKey(Row x,int index)=>x.Identity?$"conflict:{index}":x.ProviderIdentity is not null?$"id:{x.ProviderIdentity}":x.Group is not null?$"group:{x.Group}":$"row:{index}";
        var dedupGroups=allFiltered.Select((x,i)=>(Row:x,Key:DedupKey(x,i))).GroupBy(x=>x.Key,StringComparer.Ordinal).ToArray();
        var collapsed=dedupGroups.Select(g=>{
            var ordered=g.Select(x=>x.Row)
                .OrderByDescending(Reviewed)
                .ThenByDescending(x=>x.Value is DiscoveryReviewDetail)
                .ThenByDescending(x=>x.At)
                .ToArray();
            return ordered[0];
        }).ToArray();

        var boundaryFrom=f.CollectedFrom??DiscoveryDailySlots.VietnamBusinessDayBoundsUtc(DateTimeOffset.UtcNow).StartUtc;
        var totalObservations=allFiltered.Length;
        var uniqueSourcePosts=dedupGroups.Length;
        var newUniquePostsToday=collapsed.Count(x=>x.FirstAt>=boundaryFrom);
        var repeatedPosts=allFiltered.Length-newUniquePostsToday;
        var unknownDatePosts=collapsed.Count(x=>!x.HasVerifiedDate);
        var newRelevantVacancies=collapsed.Count(x=>x.Target=="TARGET_MATCH"&&x.FirstAt>=boundaryFrom);
        var repeatedRelevantVacancies=allFiltered.Count(x=>x.Target=="TARGET_MATCH")-newRelevantVacancies;
        var contentUpdatedPosts=collapsed.Count(x=>x.DuplicateStatus=="CONTENT_UPDATED");
        var exactReposts=collapsed.Count(x=>x.DuplicateStatus=="EXACT_CONTENT_REPOST");

        var selected=f.CollapseDuplicates&&!request.ExecutionId.HasValue?collapsed:allFiltered;
        var counts=new DiscoveryOpportunityCounts(selected.Length,selected.Count(x=>x.Content=="VALID_RECRUITMENT"),selected.Count(x=>x.Content=="INVALID_RECRUITMENT"),
            selected.Count(x=>x.Content is null or "NEEDS_REVIEW"),selected.Count(x=>x.Target=="TARGET_MATCH"),
            selected.Count(x=>x.Identity),selected.Count(x=>x.OccurrenceCount>1||(x.Group is not null&&groups[x.Group]>1)),selected.Count(Reviewed),selected.Count(x=>x.Content is null),
            TotalObservations:totalObservations,UniqueSourcePosts:uniqueSourcePosts,NewUniquePostsToday:newUniquePostsToday,
            RepeatedPosts:repeatedPosts,UnknownDatePosts:unknownDatePosts,NewRelevantVacancies:newRelevantVacancies,
            RepeatedRelevantVacancies:Math.Max(0,repeatedRelevantVacancies),ContentUpdatedPosts:contentUpdatedPosts,ExactContentReposts:exactReposts);
        static string SessionKey(Row x)=>x.Observation?.ExecutionId.ToString()??x.RunId;
        var sessionAt=selected.GroupBy(SessionKey,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Max(r=>r.At),StringComparer.Ordinal);
        var page=selected.OrderByDescending(x=>sessionAt[SessionKey(x)]).ThenBy(SessionKey,StringComparer.Ordinal)
            .ThenByDescending(x=>x.At)
            .ThenBy(x=>x.Observation is not null?1:(x.Value as DiscoveryReviewDetail)?.PageNumber??1)
            .ThenBy(x=>x.Observation?.ItemIndex??(x.Value as DiscoveryReviewDetail)?.Item?.ItemIndex??(x.Value as DiscoveryReviewDetail)?.Recovery?.ItemIndex??0)
            .ThenByDescending(x=>x.Target=="TARGET_MATCH"&&!Reviewed(x))
            .ThenBy(x=>(x.Value as DiscoveryReviewDetail)?.Id??x.Observation!.ExecutionId)
            .Skip((f.Page-1)*f.PageSize).Take(f.PageSize).Select(x=>x.Value).ToArray();
        return new(new(page,f.Page,f.PageSize,selected.Length),counts);
    }
}
