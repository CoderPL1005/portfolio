using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionService
{
    private async Task ValidatePersistedEvidence(DiscoveryExecution execution, CancellationToken ct)
    {
        if(execution.WorkflowRunId is null)throw Conflict("DISCOVERY_REPLAY_PROVENANCE_INVALID");
        var attempt=await AttemptAsync(execution.Id,execution.WorkflowRunId,ct);
        var events=await db.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==execution.Id&&x.State=="PROVIDER_EVIDENCE").ToArrayAsync(ct);
        if(events.Length!=1)throw Conflict("DISCOVERY_PROVIDER_NOT_SUCCESSFUL");
        var evidence=JsonSerializer.Deserialize<DiscoveryExecutionResult>(events[0].Reason!,Json);
        if(evidence is null||!RecoveryEvidenceValid(evidence)||attempt.ActualRequests!=evidence.ActualRequests||attempt.CreditsDeducted!=evidence.CreditsDeducted||
            attempt.CreditsUsedAlternateHeader!=evidence.CreditsUsedAlternateHeader||attempt.HttpStatus!=evidence.HttpStatus)throw Conflict("DISCOVERY_PROVIDER_NOT_SUCCESSFUL");
    }

    public async Task<object> ProcessAsync(Guid id,DiscoveryProcessingRequest request,string? workerRunId,CancellationToken ct)
    {
        var execution=workerRunId is null?await RequireAsync(id,ct):await WorkerAsync(id,workerRunId,ct);
        if(workerRunId is null){
            if(request.ExpectedWorkflowRunId!=execution.WorkflowRunId||request.ExpectedVersion is null)throw Conflict("DISCOVERY_REPLAY_PROVENANCE_INVALID");
            await ValidatePersistedEvidence(execution,ct);
        }else if(execution.State!="INGESTING"||execution.ResultHash is not null)throw Conflict("DISCOVERY_EXECUTION_NOT_CLAIMED");
        var row=await db.DiscoveryExecutionObservations.SingleOrDefaultAsync(x=>x.ExecutionId==id&&x.ItemIndex==request.ItemIndex,ct);
        if(row is null)throw Conflict("DISCOVERY_OBSERVATION_CONFLICT");
        using var document=JsonDocument.Parse(row.PayloadJson);var source=document.RootElement;
        var raw=Text(source,"rawContent");var originalHash=Text(source,"contentHash");
        var identity=Hash(new{itemIndex=row.ItemIndex,providerPostId=Text(source,"providerPostId"),sourceUrl=Text(source,"sourceUrl"),rawContent=raw,contentHash=originalHash,publishedAt=Text(source,"publishedAt")});
        if(row.PayloadHash!=identity||row.ContentHash!=originalHash||request.ContentHash!=originalHash||
            (raw is null?originalHash is not null:DiscoveryReviewRules.Hash(raw)!=originalHash))throw Conflict("DISCOVERY_PROCESSING_SOURCE_CONFLICT");
        if(row.RawJobPostingId is {} rawId){
            var original=await db.RawJobPostings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==rawId,ct);
            if(original is null||original.RawContent!=raw||original.ContentHash!=JobHashing.Sha256(JobHashing.NormalizeText(raw!)))throw Conflict("DISCOVERY_PROCESSING_SOURCE_CONFLICT");
        }
        var review=request.Review;
        var observedId=Text(source,"providerPostId");
        var reviewableId=observedId is not null&&System.Text.RegularExpressions.Regex.IsMatch(observedId,"^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$")?observedId:null;
        var dateText=Text(source,"publishedAt");
        var validDate=dateText is null||DateTimeOffset.TryParse(dateText,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _);
        var publishedAt=dateText is null||!validDate?(DateTimeOffset?)null:DateTimeOffset.Parse(dateText,System.Globalization.CultureInfo.InvariantCulture);
        // A validation envelope only: never persisted as an invented original query
        // or normal ReviewBundle. Processing stays a separate, newly computed layer.
        if(request.Vacancies is null||request.Vacancies.Length>30||request.ExtractionError is not(null or "SEMANTIC_UNAVAILABLE" or "DETERMINISTIC_EXTRACTION_FAILED" or "CLASSIFICATION_FAILED"))throw Invalid("processing");
        if(review is null){
            if(request.ExtractionError!="CLASSIFICATION_FAILED"||request.Vacancies.Length!=0)throw Invalid("processing");
        }else if(request.ExtractionError=="CLASSIFICATION_FAILED"||review.ItemIndex!=row.ItemIndex||!DiscoveryReviewRules.Valid(new(1,"SOCIALAPIS",id.ToString(),"persisted-processing-validation",1,[review with {ItemIndex=0}]))||
            review.SourceUrl!=Text(source,"sourceUrl")||review.RawContent!=raw||review.ContentHash!=originalHash||
            review.ProviderPostId!=reviewableId||!validDate||review.PublishedAt!=publishedAt||
            Text(source,"outcome")=="POST_ID_URL_MISMATCH"&&(!review.SourceWarnings.Contains("POST_ID_URL_MISMATCH")||review.SourceUrl is not null))throw Invalid("processing");
        var fingerprint=Hash(new{review=review is null?null:review with {EvaluatedAt=default},request.Vacancies,request.ExtractionError});
        if(source.TryGetProperty("processing",out var prior)&&Text(prior,"fingerprint")==fingerprint)return new{version=execution.Version,stored=true};
        if(workerRunId is null&&request.ExpectedVersion!=execution.Version)throw Conflict("DISCOVERY_EXECUTION_CONFLICT");
        var vacancies=request.Vacancies;var text=raw??"";
        var starts=vacancies.Select(v=>Text(v,"positionTitle") is {Length:>0} title?text.IndexOf(title,StringComparison.Ordinal):-1).ToArray();
        var mapped=vacancies.Select((v,i)=>{
            var start=vacancies.Length==1?0:starts.Any(x=>x<0)||starts.Distinct().Count()!=starts.Length?-1:starts[i];
            var end=vacancies.Length==1?text.Length:starts.Where(x=>x>start).DefaultIfEmpty(text.Length).Min();
            var m=MapVacancy(v,text,vacancies.Length==1,start,end);
            return new{fields=m.Fields,missingFields=m.Missing,status="NEEDS_REVIEW"};
        }).ToArray();
        var payload=JsonNode.Parse(row.PayloadJson)!;
        payload["processing"]=JsonSerializer.SerializeToNode(new{origin=workerRunId is null?"PERSISTED_REPROCESSING":"WORKER_INCREMENTAL",fingerprint,
            review,vacancies=mapped,request.ExtractionError,processedAt=clock.GetUtcNow()},Json);
        row.PayloadJson=payload.ToJsonString();execution.Version++;execution.UpdatedAt=clock.GetUtcNow();
        // One save/transaction, guarded by the execution concurrency token. No
        // claim release, accounting write, normal candidate overwrite or promotion.
        await SaveAsync(ct);return new{version=execution.Version,stored=true};
    }

    public async Task<object> ExtractPersistedAsync(Guid id,DiscoveryPersistedSemanticRequest request,CancellationToken ct)
    {
        var execution=await RequireAsync(id,ct);
        if(!request.ConfirmGeminiFreeTier||!Settings.SemanticEnabled||!Settings.GeminiFreeTierConfirmed)throw Conflict("DISCOVERY_SEMANTIC_DISABLED");
        if(execution.Version!=request.ExpectedVersion||execution.WorkflowRunId!=request.ExpectedWorkflowRunId)throw Conflict("DISCOVERY_EXECUTION_CONFLICT");
        await ValidatePersistedEvidence(execution,ct);
        try{
            var generation=await ExtractSavedAsync(execution,new(request.ItemIndex,request.RawContent,request.ContentHash),ct);
            return new{version=execution.Version,generation};
        }catch(Portfolio.Application.Common.Exceptions.ServiceUnavailableException){
            // Reservation and UNKNOWN were committed: return that new version so
            // the caller can still persist classification, without retrying Gemini.
            return new{version=execution.Version,generation=(DiscoveryTextGeneration?)null,errorCode="SEMANTIC_UNAVAILABLE"};
        }
    }
}
