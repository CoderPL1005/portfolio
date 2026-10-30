using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionService
{
    internal static bool RecoveryEvidenceValid(DiscoveryExecutionResult evidence) => evidence.ActualRequests == 1 && evidence.ProviderOperation == "SUCCEEDED" && ProviderSucceeded(evidence);
    private static readonly string[] PostingFields = ["companyName","positionTitle","location","employmentType","workplaceType","salaryMinimum","salaryMaximum",
        "salaryCurrency","salaryPeriod","experienceRequirements","description","technologyStack","applicationEmail","applicationUrl","expiresAt","notes"];
    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static JsonElement[] Array(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x=>x.Clone()).ToArray() : [];
    private static string Canonical(JsonElement e) => e.ValueKind switch {
        JsonValueKind.Object => "{" + string.Join(",",e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",",e.EnumerateArray().Select(Canonical)) + "]", _ => e.GetRawText() };
    private static string Hash(object value) => DiscoveryReviewRules.Hash(Canonical(JsonSerializer.SerializeToElement(value,Json)));
    private static void ValidateResult(DiscoveryExecutionResult result)
    {
        if(result.ProviderOperation is not(null or "SUCCEEDED" or "FAILED" or "UNKNOWN") ||
            result.ErrorCode is not null && !Regex.IsMatch(result.ErrorCode,"^[A-Z][A-Z0-9_]{0,99}$") ||
            result.UsageEvidence is { } usage && (usage.BodyCreditsCharged.ValueKind is JsonValueKind.Undefined || usage.BodyCreditsCharged.GetRawText().Length>1024 || usage.DeductedHeader?.Length>128||usage.AlternateHeader?.Length>128))throw Invalid("providerEvidence");
        if (result.ActualRequests != 1 || result.CreditsDeducted < 0 || result.CreditsUsedAlternateHeader < 0 || result.Observations is null || result.Observations.Length > 200 ||
            JsonSerializer.SerializeToUtf8Bytes(result,Json).Length > 8*1024*1024) throw Invalid("result");
        foreach (var (item,index) in result.Observations.Select((x,i)=>(x,i)))
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("itemIndex",out var n) || !n.TryGetInt32(out var number) || number != index ||
                Text(item,"rawContent") is { } raw && (raw.Length > 1048576 || raw.Contains('\0') || Text(item,"contentHash") != DiscoveryReviewRules.Hash(raw))) throw Invalid("observations");
    }
    private static bool KnownCost(DiscoveryExecutionResult result)=>result.CreditsDeducted is >=0 and <=1 &&
        (result.UsageEvidence is not { } evidence ||
            (evidence.DeductedHeader is null || Regex.IsMatch(evidence.DeductedHeader,"^[0-9]+(?:\\.[0-9]+)?$")&&
                decimal.TryParse(evidence.DeductedHeader,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var headerCost)&&headerCost==result.CreditsDeducted)&&
            (evidence.BodyCostPresent?evidence.BodyCreditsCharged.ValueKind==JsonValueKind.Number&&evidence.BodyCreditsCharged.TryGetDecimal(out var bodyCost)&&bodyCost==result.CreditsDeducted:
                evidence.BodyCreditsCharged.ValueKind==JsonValueKind.Null));
    private static bool ProviderSucceeded(DiscoveryExecutionResult result)=>KnownCost(result)&&result.HttpStatus==200&&
        (result.ProviderOperation=="SUCCEEDED"||result.ProviderOperation is null&&result.UsageEvidence is null&&
            result.Observations?.Any(item=>Promotable(item,true))==true) && result.State is not("FAILED" or "PROVIDER_UNKNOWN")&&result.ErrorCode is null;
    private async Task RecordProviderEvidenceAsync(DiscoveryExecution execution,DiscoveryExecutionResult result,CancellationToken ct)
    {
        // Append-only and retained by Reset All, independently of dispatch config.
        var evidence=JsonSerializer.Serialize(new{result.ProviderOperation,result.ErrorCode,result.ActualRequests,result.HttpStatus,
            result.CreditsDeducted,result.CreditsUsedAlternateHeader,result.UsageEvidence},Json);
        var prior=await db.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==execution.Id&&x.State=="PROVIDER_EVIDENCE").Select(x=>x.Reason).SingleOrDefaultAsync(ct);
        if(prior is not null){if(prior!=evidence)throw Conflict("DISCOVERY_USAGE_CONFLICT");return;}
        db.DiscoveryExecutionEvents.Add(new(){Id=Guid.NewGuid(),ExecutionId=execution.Id,State="PROVIDER_EVIDENCE",Reason=evidence,At=clock.GetUtcNow()});
    }
    private static bool Promotable(JsonElement item,bool legacy)
    {
        // Older callbacks omitted outcome. Independent strict URL/ID checks still
        // apply; any explicit unsupported or unknown outcome is staging only.
        var outcome=Text(item,"outcome");
        if(outcome!="READY_FOR_EXTRACTION"&&!(legacy&&outcome is null))return false;
        var url=Text(item,"sourceUrl");var id=Text(item,"providerPostId");
        return url is not null&&id is not null&&DiscoveryReviewRules.SafeUrl(url)&&
            new Uri(url).AbsolutePath.TrimEnd('/').Split('/').Last()==id;
    }
    private async Task<DiscoveryExecutionAttempt> AttemptAsync(Guid id, string runId, CancellationToken ct)
    {
        var attempt=await db.DiscoveryExecutionAttempts.SingleOrDefaultAsync(x=>x.ExecutionId==id,ct);
        if(attempt is null || attempt.WorkflowRunId!=runId)throw Conflict("DISCOVERY_PROVIDER_NOT_CLAIMED");return attempt;
    }
    private static void Usage(DiscoveryExecutionAttempt attempt, DiscoveryExecutionResult result)
    {
        // Never replace observed/unknown charge evidence with an invented zero.
        if(attempt.ActualRequests is not null && attempt.ActualRequests!=result.ActualRequests ||
            attempt.CreditsDeducted is not null && attempt.CreditsDeducted!=result.CreditsDeducted ||
            attempt.HttpStatus is not null && attempt.HttpStatus!=result.HttpStatus)throw Conflict("DISCOVERY_USAGE_CONFLICT");
        attempt.ActualRequests=result.ActualRequests;attempt.CreditsDeducted=result.CreditsDeducted;
        attempt.CreditsUsedAlternateHeader=result.CreditsUsedAlternateHeader;attempt.HttpStatus=result.HttpStatus;
    }
    private async Task<List<DiscoveryExecutionObservation>> StoreRawAsync(DiscoveryExecution execution, JsonElement[] items, CancellationToken ct)
    {
        var rows=await db.DiscoveryExecutionObservations.Where(x=>x.ExecutionId==execution.Id).OrderBy(x=>x.ItemIndex).ToListAsync(ct);
        if(rows.Count>0 && rows.Count!=items.Length)throw Conflict("DISCOVERY_OBSERVATION_CONFLICT");
        foreach(var item in items)
        {
            var index=item.GetProperty("itemIndex").GetInt32();var raw=Text(item,"rawContent");
            var providerId=Text(item,"providerPostId");var url=Text(item,"sourceUrl");var contentHash=Text(item,"contentHash");
            var identity=Hash(new{itemIndex=index,providerPostId=providerId,sourceUrl=url,rawContent=raw,contentHash,publishedAt=Text(item,"publishedAt")});
            var existing=rows.SingleOrDefault(x=>x.ItemIndex==index);
            if(existing is not null){if(existing.PayloadHash!=identity||Text(JsonDocument.Parse(existing.PayloadJson).RootElement,"providerSourceUrl")!=Text(item,"providerSourceUrl"))throw Conflict("DISCOVERY_OBSERVATION_CONFLICT");continue;}
            RawJobPosting? source=null;
            if(!string.IsNullOrWhiteSpace(raw))
            {
                var validId=providerId is not null && Regex.IsMatch(providerId,"^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$");
                var ingestionKey="discovery:"+DiscoveryReviewRules.Hash((providerId??"unknown")+"\n"+contentHash);
                source=db.RawJobPostings.Local.FirstOrDefault(x=>x.IngestionKey==ingestionKey)??await db.RawJobPostings.SingleOrDefaultAsync(x=>x.IngestionKey==ingestionKey,ct);
                if(source is null && validId)source=db.RawJobPostings.Local.SingleOrDefault(x=>x.Source=="FACEBOOK"&&x.SourceExternalId==providerId)??await db.RawJobPostings.SingleOrDefaultAsync(x=>x.Source=="FACEBOOK"&&x.SourceExternalId==providerId,ct);
                // Existing raw content is immutable. Changed-content observations retain
                // original provider identity in metadata, not a fabricated external ID.
                if(source is null || source.ContentHash!=JobHashing.Sha256(JobHashing.NormalizeText(raw)))
                {
                    var occupied=source is not null;var safeUrl=DiscoveryReviewRules.SafeUrl(url)?url:null;
                    var urlHash=safeUrl is null?null:JobHashing.Sha256(JobHashing.NormalizeUrl(safeUrl));
                    if(urlHash is not null && (db.RawJobPostings.Local.Any(x=>x.Source=="FACEBOOK"&&x.SourceUrlHash==urlHash)||await db.RawJobPostings.AnyAsync(x=>x.Source=="FACEBOOK"&&x.SourceUrlHash==urlHash,ct)))safeUrl=null;
                    var now=clock.GetUtcNow();source=new(){Id=Guid.NewGuid(),Source="FACEBOOK",SourceExternalId=validId&&!occupied?providerId:null,
                        IngestionKey=ingestionKey,SourceUrl=safeUrl,SourceUrlHash=safeUrl is null?null:urlHash,RawContent=raw,
                        ContentHash=JobHashing.Sha256(JobHashing.NormalizeText(raw)),Metadata=JsonDocument.Parse(JsonSerializer.Serialize(new{providerPostId=providerId,
                            sourceUrl=url,providerSourceUrl=Text(item,"providerSourceUrl"),exactRawContentHash=contentHash,executionId=execution.Id,identityVerified=false},Json)),
                        DiscoveredAt=now,CreatedAt=now,UpdatedAt=now};db.RawJobPostings.Add(source);
                }
            }
            var observation=new DiscoveryExecutionObservation{Id=Guid.NewGuid(),ExecutionId=execution.Id,ItemIndex=index,
                PayloadJson=item.GetRawText(),PayloadHash=identity,ContentHash=contentHash,RawJobPostingId=source?.Id};
            db.DiscoveryExecutionObservations.Add(observation);rows.Add(observation);
        }
        return rows;
    }
    public async Task<object> ObservationsAsync(Guid id,string runId,DiscoveryExecutionResult result,CancellationToken ct)
    {
        ValidateResult(result);var execution=await WorkerAsync(id,runId,ct);var attempt=await AttemptAsync(id,runId,ct);
        if(execution.ResultHash is not null)throw Conflict("DISCOVERY_EXECUTION_COMPLETED");
        if(execution.State is not("CLAIMED" or "INGESTING"))throw Conflict("DISCOVERY_EXECUTION_NOT_CLAIMED");
        Usage(attempt,result);await RecordProviderEvidenceAsync(execution,result,ct);await StoreRawAsync(execution,result.Observations,ct);Transition(execution,"INGESTING");
        await SaveAsync(ct);return new{stored=result.Observations.Length};
    }
    public async Task<object> CompleteAsync(Guid id,string runId,DiscoveryExecutionResult result,CancellationToken ct)
    {
        ValidateResult(result);var execution=await WorkerAsync(id,runId,ct);var attempt=await AttemptAsync(id,runId,ct);
        var resultHash=Hash(result);
        if(execution.ResultHash is not null){if(execution.ResultHash!=resultHash)throw Conflict("DISCOVERY_RESULT_CONFLICT");return await ViewAsync(execution,ct);}
        if(execution.State is not("CLAIMED" or "INGESTING"))throw Conflict("DISCOVERY_EXECUTION_NOT_CLAIMED");
        Usage(attempt,result);await RecordProviderEvidenceAsync(execution,result,ct);var observations=await StoreRawAsync(execution,result.Observations,ct);var missing=0;
        var knownCost=KnownCost(result);var providerSucceeded=ProviderSucceeded(result);
        foreach(var observation in observations)
        {
            var item=result.Observations[observation.ItemIndex];var raw=Text(item,"rawContent")??"";
            // Keep latest validated extraction as a separate layer, never rewrite raw.
            var merged=System.Text.Json.Nodes.JsonNode.Parse(item.GetRawText())!;
            var saved=System.Text.Json.Nodes.JsonNode.Parse(observation.PayloadJson)!;
            if(saved["processing"] is {} processing)merged["processing"]=processing.DeepClone();
            observation.PayloadJson=merged.ToJsonString();
            var vacancies=Array(item,"vacancies");
            if(vacancies.Length>30)throw Invalid("vacancies");
            var starts=vacancies.Select(v=>Text(v,"positionTitle") is {Length:>0} title?raw.IndexOf(title,StringComparison.Ordinal):-1).ToArray();
            for(var i=0;i<vacancies.Length;i++)
            {
                var scopeStart=vacancies.Length==1?0:starts[i];
                var scopeEnd=vacancies.Length==1?raw.Length:starts.Where(x=>x>scopeStart).DefaultIfEmpty(raw.Length).Min();
                // Ambiguous/missing/duplicate headings cannot attribute arrays.
                if(vacancies.Length>1&&(starts.Any(x=>x<0)||starts.Distinct().Count()!=starts.Length))scopeStart=-1;
                var vacancy=vacancies[i];var mapped=MapVacancy(vacancy,raw,vacancies.Length==1,scopeStart,scopeEnd);var fields=mapped.Fields;
                var fingerprint=Hash(new{contentHash=observation.ContentHash,fields});
                var row=new DiscoveryExecutionVacancy{Id=Guid.NewGuid(),ObservationId=observation.Id,VacancyIndex=i,
                    RawJobPostingId=observation.RawJobPostingId,Fingerprint=fingerprint,FieldsJson=JsonSerializer.Serialize(fields,Json),
                    MissingFieldsJson=JsonSerializer.Serialize(mapped.Missing,Json)};
                // Discovery supplies reviewable evidence only. Creating a JobPosting
                // remains an explicit user action, even for a fully extracted JD.
                missing++;
                db.DiscoveryExecutionVacancies.Add(row);
            }
            if(vacancies.Length==0)missing++;
        }
        execution.ResultHash=resultHash;
        await StoreReviewBundleAsync(execution,result,ct);
        var missingOperation=result.ProviderOperation is null&&result.HttpStatus==200&&!providerSucceeded&&result.ErrorCode is null&&result.State is not("FAILED" or "PROVIDER_UNKNOWN");
        var state=!knownCost||missingOperation||result.ProviderOperation=="UNKNOWN"||result.State=="PROVIDER_UNKNOWN"?"PROVIDER_UNKNOWN":
            !providerSucceeded?"FAILED":missing>0?"PARTIAL":"SUCCEEDED";
        attempt.Outcome=state;Transition(execution,state,result.ErrorCode??(state=="PROVIDER_UNKNOWN"?"DISCOVERY_COST_OR_OUTCOME_UNKNOWN":null));
        if(state!="PROVIDER_UNKNOWN")execution.ActiveKey=null;
        await SaveAsync(ct);return await ViewAsync(execution,ct);
    }

    private async Task StoreReviewBundleAsync(DiscoveryExecution execution,DiscoveryExecutionResult result,CancellationToken ct)
    {
        // Optional for older worker callbacks; new worker uses the existing immutable
        // review contract. Persistence shares the final execution transaction.
        var bundle=result.ReviewBundle;if(bundle is null)return;
        if(!DiscoveryReviewRules.Valid(bundle)||bundle.RunId!=execution.Id.ToString()||bundle.PageNumber!=1||bundle.Items.Length!=result.Observations.Length)
            throw Invalid("reviewBundle");
        var existing=await db.DiscoveryCandidates.Where(c=>c.RunId==bundle.RunId).ToArrayAsync(ct);
        foreach(var item in bundle.Items)
        {
            var observation=result.Observations[item.ItemIndex];
            // Review cannot substitute another raw source or attach another execution.
            var raw=Text(observation,"rawContent");
            var observedId=Text(observation,"providerPostId");
            var reviewableId=observedId is not null&&Regex.IsMatch(observedId,"^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$")?observedId:null;
            if(item.RawContent is not null && item.RawContent!=raw || item.ProviderPostId!=reviewableId || item.SourceUrl!=Text(observation,"sourceUrl"))throw Invalid("reviewBundle");
            var payload=JsonSerializer.Serialize(item,DiscoveryReviewRules.Json);
            var hash=DiscoveryReviewRules.Hash(bundle.Query+"\n"+payload);
            var prior=existing.SingleOrDefault(c=>c.PageNumber==1&&c.ItemIndex==item.ItemIndex);
            if(prior is not null){if(prior.PayloadHash!=hash)throw Conflict("DISCOVERY_IMPORT_CONFLICT");continue;}
            db.DiscoveryCandidates.Add(new(){Id=Guid.NewGuid(),RunId=bundle.RunId,Query=bundle.Query,PageNumber=1,ItemIndex=item.ItemIndex,
                ContentStatus=item.ContentValidity.Status,TargetStatus=item.TargetMatching.Status,ContentGroupHash=item.ContentGroupHash,
                IdentityWarning=item.SourceWarnings.Contains("POST_ID_URL_MISMATCH"),PayloadHash=hash,Payload=payload,IngestedAt=clock.GetUtcNow()});
        }
    }

    private sealed record MappedVacancy(Dictionary<string,object> Fields,string[] Missing,CreateJobPostingCommand? Command);
    private static MappedVacancy MapVacancy(JsonElement value,string raw,bool unambiguousRole,int scopeStart,int scopeEnd)
    {
        var exact=unambiguousRole?DiscoveryPostingFacts.Extract(raw):new Dictionary<string,DiscoveryPostingFacts.Fact>();
        var fields=new Dictionary<string,object>();var values=new Dictionary<string,JsonElement>();var missing=new List<string>();
        foreach(var key in PostingFields)
        {
            var element=value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var v)?v.Clone():JsonSerializer.SerializeToElement<object?>(null);
            // Null/unsupported facts abstain; evidence is exact source text, never a score.
            var present=element.ValueKind!=JsonValueKind.Null&&element.ValueKind!=JsonValueKind.Undefined;
            var quote=element.ValueKind==JsonValueKind.String?element.GetString():null;
            var supported=present&&(quote is null||raw.Contains(quote,StringComparison.OrdinalIgnoreCase));
            var arraySpans=key=="technologyStack"&&value.TryGetProperty("fields",out var supplied)&&supplied.ValueKind==JsonValueKind.Object&&supplied.TryGetProperty(key,out var sf)?Array(sf,"evidence"):[];
            bool ValidSpan(JsonElement span)=>span.TryGetProperty("start",out var s)&&s.TryGetInt32(out var a)&&span.TryGetProperty("end",out var e)&&e.TryGetInt32(out var b)&&
                scopeStart>=0&&a>=scopeStart&&b>a&&b<=scopeEnd&&Text(span,"quote") is { } q&&raw[a..b]==q;
            if(key=="technologyStack")supported=element.ValueKind==JsonValueKind.Array&&element.GetArrayLength()>0&&arraySpans.Length is >0 and <=40&&arraySpans.All(x=>x.ValueKind==JsonValueKind.Object&&ValidSpan(x))&&
                element.EnumerateArray().All(x=>x.ValueKind==JsonValueKind.String&&arraySpans.Any(s=>Text(s,"quote")!.Contains(x.GetString()!,StringComparison.OrdinalIgnoreCase)));
            if(key is "salaryMinimum" or "salaryMaximum" or "salaryCurrency" or "salaryPeriod" or "expiresAt")
            {
                supported=exact.TryGetValue(key,out var fact)&&Canonical(JsonSerializer.SerializeToElement(fact.Value))==Canonical(element);
                quote=supported?fact!.Quote:null;
            }
            else if(element.ValueKind==JsonValueKind.Number)supported=false;
            if(!supported)element=JsonSerializer.SerializeToElement<object?>(null);
            values[key]=element;
            var status=!present?"NOT_PROVIDED":supported?"EXTRACTED":"NEEDS_REVIEW";
            var start=supported&&quote is not null?raw.IndexOf(quote,StringComparison.OrdinalIgnoreCase):-1;
            fields[key]=new{value=element,status,evidence=key=="technologyStack"&&supported?(object)arraySpans:start>=0?new[]{new{start,end=start+quote!.Length,quote=raw.Substring(start,quote.Length)}}:[]};
            if(key is "companyName" or "positionTitle" or "location" or "description" && (!supported||element.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(element.GetString())))missing.Add(key);
        }
        string? S(string name)=>values[name].ValueKind==JsonValueKind.String?values[name].GetString():null;
        decimal? N(string name)=>values[name].ValueKind==JsonValueKind.Number?values[name].GetDecimal():null;
        var stack=values["technologyStack"].ValueKind==JsonValueKind.Array?values["technologyStack"]:JsonSerializer.SerializeToElement(System.Array.Empty<string>());
        var command=new CreateJobPostingCommand("FACEBOOK",null,null,raw,S("companyName")??"",S("positionTitle")??"",S("location")??"",S("employmentType"),S("workplaceType"),
            N("salaryMinimum"),N("salaryMaximum"),S("salaryCurrency"),S("salaryPeriod"),S("experienceRequirements"),S("description")??"",stack,S("applicationEmail"),S("applicationUrl"),S("expiresAt") is { } expires?DateTimeOffset.Parse(expires,System.Globalization.CultureInfo.InvariantCulture):null,S("notes"));
        var invalid=JobValidation.Posting(command.Source,null,null,raw,command.CompanyName,command.PositionTitle,command.Location,command.EmploymentType,command.WorkplaceType,
            command.SalaryMinimum,command.SalaryMaximum,command.SalaryCurrency,command.SalaryPeriod,command.Description,stack,command.ApplicationEmail,command.ApplicationUrl,command.Notes);
        missing.AddRange(invalid.Select(x=>x.PropertyName));return new(fields,missing.Distinct().ToArray(),missing.Count==0?command:null);
    }
}
