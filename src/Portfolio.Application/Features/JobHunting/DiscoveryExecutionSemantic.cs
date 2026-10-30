using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionService
{
    public async Task<DiscoveryTextGeneration> ExtractAsync(Guid id,string runId,DiscoverySemanticRequest request,CancellationToken ct)
    {
        if(request.RawContent is null||request.RawContent.Length>24000||request.ContentHash!=DiscoveryReviewRules.Hash(request.RawContent))throw Invalid("rawContent");
        var execution=await WorkerAsync(id,runId,ct);var attempt=await AttemptAsync(id,runId,ct);
        var evidence=await db.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==id&&x.State=="PROVIDER_EVIDENCE").Select(x=>x.Reason).SingleOrDefaultAsync(ct);
        if(attempt.CreditsDeducted is not(>=0 and <=1)||attempt.HttpStatus!=200||evidence is null)throw Conflict("DISCOVERY_PROVIDER_NOT_SUCCESSFUL");
        var recorded=JsonSerializer.Deserialize<DiscoveryExecutionResult>(evidence,Json);
        if(recorded is null||!ProviderSucceeded(recorded))throw Conflict("DISCOVERY_PROVIDER_NOT_SUCCESSFUL");
        if(execution.State!="INGESTING"||!Settings.SemanticEnabled||!Settings.GeminiFreeTierConfirmed)throw Conflict("DISCOVERY_SEMANTIC_DISABLED");
        using var config=JsonDocument.Parse(execution.ConfigurationJson);
        if(!config.RootElement.TryGetProperty("semanticEnabled",out var enabled)||enabled.ValueKind!=JsonValueKind.True||Text(config.RootElement,"semanticModel")!=semantic.ModelIdentity)throw Conflict("DISCOVERY_SEMANTIC_DISABLED");
        return await ExtractSavedAsync(execution,request,ct);
    }
    private async Task<DiscoveryTextGeneration> ExtractSavedAsync(Portfolio.Domain.Entities.DiscoveryExecution execution,DiscoverySemanticRequest request,CancellationToken ct)
    {
        if(request.RawContent is null||request.RawContent.Length>24000||request.ContentHash!=DiscoveryReviewRules.Hash(request.RawContent))throw Invalid("rawContent");
        var id=execution.Id;
        var observation=await db.DiscoveryExecutionObservations.SingleOrDefaultAsync(x=>x.ExecutionId==id&&x.ItemIndex==request.ItemIndex,ct);
        if(observation is null||observation.ContentHash!=request.ContentHash||Text(JsonDocument.Parse(observation.PayloadJson).RootElement,"rawContent")!=request.RawContent)throw Conflict("DISCOVERY_SEMANTIC_SOURCE_CONFLICT");
        using var payload=JsonDocument.Parse(observation.PayloadJson);var source=payload.RootElement;
        if(observation.PayloadHash!=Hash(new{itemIndex=observation.ItemIndex,providerPostId=Text(source,"providerPostId"),sourceUrl=Text(source,"sourceUrl"),rawContent=Text(source,"rawContent"),contentHash=Text(source,"contentHash"),publishedAt=Text(source,"publishedAt")}))throw Conflict("DISCOVERY_SEMANTIC_SOURCE_CONFLICT");
        if(!source.TryGetProperty("processing",out var processing)||!processing.TryGetProperty("review",out var review)||review.ValueKind!=JsonValueKind.Object||
            !review.TryGetProperty("contentValidity",out var validity)||Text(validity,"status")!="VALID_RECRUITMENT"||
            Text(source,"outcome")!="READY_FOR_EXTRACTION")throw Conflict("DISCOVERY_SEMANTIC_RECRUITMENT_REQUIRED");
        if(!semantic.IsConfigured)throw Conflict("DISCOVERY_SEMANTIC_DISABLED");
        var cacheKey=Hash(new{request.ContentHash,model=semantic.ModelIdentity,contract="ai-job-extraction-v1/ai-extraction-prompt-v1"});
        if(observation.SemanticState=="COMPLETED"&&observation.SemanticCacheKey==cacheKey&&observation.SemanticOutputJson is not null)
            return JsonSerializer.Deserialize<DiscoveryTextGeneration>(observation.SemanticOutputJson,Json)!;
        if(observation.SemanticState!="NOT_REQUESTED")throw Conflict("DISCOVERY_SEMANTIC_BUDGET_OR_ATTEMPT_USED");
        var cached=await db.DiscoveryExecutionObservations.AsNoTracking().FirstOrDefaultAsync(x=>x.SemanticState=="COMPLETED"&&x.SemanticCacheKey==cacheKey&&x.SemanticOutputJson!=null,ct);
        if(cached is not null){
            observation.SemanticCacheKey=cacheKey;observation.SemanticOutputJson=cached.SemanticOutputJson;observation.SemanticState="COMPLETED";
            execution.Version++;await SaveAsync(ct);
            return JsonSerializer.Deserialize<DiscoveryTextGeneration>(cached.SemanticOutputJson!,Json)!;
        }
        if(execution.SemanticAttempts>=Math.Clamp(Settings.MaximumSemanticAttempts,1,5))throw Conflict("DISCOVERY_SEMANTIC_BUDGET_OR_ATTEMPT_USED");
        // Commit reservation before external generation; crashes never auto-retry.
        observation.SemanticState="CLAIMED";observation.SemanticCacheKey=cacheKey;execution.SemanticAttempts++;
        execution.Version++;execution.UpdatedAt=clock.GetUtcNow();await SaveAsync(ct);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result=await semantic.ExtractAsync(request.RawContent,timeout.Token);
            observation.SemanticOutputJson=JsonSerializer.Serialize(result,Json);observation.SemanticState="COMPLETED";execution.Version++;
            await SaveAsync(ct);return result;
        }
        catch
        {
            observation.SemanticState="UNKNOWN";execution.Version++;
            await SaveAsync(CancellationToken.None);
            throw new ServiceUnavailableException("DISCOVERY_SEMANTIC_UNAVAILABLE","Semantic extraction is unavailable. Raw observations are preserved for review.");
        }
    }
}
