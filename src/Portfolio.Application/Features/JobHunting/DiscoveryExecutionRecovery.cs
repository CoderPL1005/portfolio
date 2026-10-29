using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

// Deliberately has no provider, semantic, GitHub or dispatch dependencies.
public sealed class DiscoveryExecutionRecovery(IApplicationDbContext db, IDiscoveryRecoveryTransactions transactions,
    IDiscoveryRecoveryReadiness readiness, ICurrentUser user, TimeProvider clock) : IDiscoveryExecutionRecovery
{
    private static readonly JsonSerializerOptions Json = DiscoveryReviewRules.Json;
    private const string Reason = "RECOVERED_FOR_HUMAN_REVIEW";
    private sealed record Snapshot(DiscoveryExecution Execution, DiscoveryExecutionAttempt[] Attempts,
        DiscoveryExecutionObservation[] Observations, RawJobPosting[] Raw, DiscoveryExecutionEvent[] Events,
        DiscoveryCandidate[] Candidates, DiscoveryCandidateReview[] Reviews, DiscoveryCandidateReviewEvent[] ReviewEvents,
        DiscoveryExecutionVacancy[] Vacancies, JobPosting[] Jobs);
    private sealed record Receipt(Guid ReceiptId, Guid ExecutionId, Guid OperationId, Guid ActorId, Guid AuditId,
        string RequestFingerprint, string PreviewFingerprint, int Version, string State, string Reason, int CandidateCount, DateTimeOffset At);

    private Guid Actor() => user.IsAuthenticated && user.AdminUserId is { } id && id != Guid.Empty ? id
        : throw new UnauthorizedException("UNAUTHORIZED", "An authenticated Admin is required.");
    private static ConflictException Conflict(string code = "DISCOVERY_RECOVERY_CONFLICT") => new(code, "Recovery is blocked or its confirmation no longer matches. Reload the preview.");

    public async Task<DiscoveryRecoveryPreview> PreviewAsync(Guid id, CancellationToken ct)
    {
        Actor(); await using var tx = await transactions.BeginAsync(false, ct);
        return Evaluate(await Load(id, ct));
    }

    public async Task<JsonElement> FinalizeAsync(Guid id, DiscoveryRecoveryFinalize request, CancellationToken ct)
    {
        var actor = Actor();
        if (request.OperationId is null || request.OperationId == Guid.Empty || request.ExpectedVersion is null ||
            string.IsNullOrWhiteSpace(request.ExpectedWorkflowRunId) || request.PreviewFingerprint is null || !Regex.IsMatch(request.PreviewFingerprint, "^[0-9a-f]{64}$") ||
            request.Confirmation != "RECOVER PERSISTED OBSERVATIONS" || request.Reason is not { Length: > 0 and <= 2000 } ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Contains('\0'))
            throw new ValidationException([new("recovery", "An explicit operation, version, run, fingerprint, confirmation and reason are required.")]);
        var requestHash = DiscoveryRecoveryProvenance.Hash(new { id, actor, request });
        await using var tx = await transactions.BeginAsync(true, ct);
        // Lookup precedes stale-version checks. No save/transition on exact replay,
        // and later human labels are not reinterpreted or overwritten.
        var saved = await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.State == DiscoveryRecoveryProvenance.ReceiptState)
            .OrderBy(x => x.Id).Take(10001).ToArrayAsync(ct);
        if (saved.Length > 10000) throw Conflict("DISCOVERY_RECOVERY_HISTORY_BOUND");
        foreach (var row in saved) {
            Receipt receipt;
            try { receipt = JsonSerializer.Deserialize<Receipt>(row.Reason!, Json) ?? throw Conflict(); }
            catch (Exception error) when(error is JsonException or ArgumentException) { throw Conflict("DISCOVERY_RECOVERY_RECEIPT_INVALID"); }
            if(receipt.ReceiptId!=row.Id||receipt.ExecutionId!=row.ExecutionId||receipt.AuditId==Guid.Empty||receipt.OperationId==Guid.Empty||
                receipt.ActorId==Guid.Empty||receipt.State!="PARTIAL"||receipt.Reason!=Reason||receipt.Version<1||receipt.At==default||receipt.CandidateCount is <1 or >200)
                throw Conflict("DISCOVERY_RECOVERY_RECEIPT_INVALID");
            if (receipt.OperationId == request.OperationId) {
                if (receipt.ExecutionId != id || receipt.ActorId != actor || receipt.RequestFingerprint != requestHash || row.ExecutionId != id || row.Id != receipt.ReceiptId)
                    throw Conflict();
                return JsonDocument.Parse(row.Reason!).RootElement.Clone();
            }
            if (row.ExecutionId == id) throw Conflict();
        }
        if (!await db.DiscoveryExecutions.AnyAsync(x => x.Id == id, ct)) throw Conflict();
        var snapshot = await Load(id, ct); var preview = Evaluate(snapshot);
        if (snapshot.Execution.Version != request.ExpectedVersion || snapshot.Execution.WorkflowRunId != request.ExpectedWorkflowRunId ||
            preview.PreviewFingerprint != request.PreviewFingerprint || preview.Status != "RECOVERABLE") throw Conflict();

        var now = clock.GetUtcNow(); var auditId = Guid.NewGuid(); var receiptId = Guid.NewGuid();
        foreach (var o in snapshot.Observations) {
            using var sourceDocument=JsonDocument.Parse(o.PayloadJson);var source = sourceDocument.RootElement; var raw = Text(source, "rawContent")!;
            var warnings = Text(source, "outcome") == "POST_ID_URL_MISMATCH" ? new[] { "POST_ID_URL_MISMATCH" } : [];
            var envelope = new DiscoveryRecoveryEnvelope(DiscoveryRecoveryProvenance.Origin, DiscoveryRecoveryProvenance.Version,
                id, o.Id, snapshot.Execution.WorkflowRunId!, request.OperationId.Value, receiptId, auditId, o.ItemIndex, o.PayloadHash,
                null, null, null, Text(source, "providerPostId")!, Text(source, "sourceUrl"), Text(source, "providerSourceUrl"), raw, o.ContentHash!,
                DiscoveryReviewRules.Hash(raw.Normalize().Replace("\r\n", "\n").Replace('\r', '\n').Trim()),
                Text(source, "publishedAt") is { } at ? DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture) : null,
                new("NEEDS_REVIEW", "INSUFFICIENT_DATA", "NOT_CHECKED"), warnings, ["ORIGINAL_QUERY", "ORIGINAL_CLASSIFIER", "ORIGINAL_REVIEW_BUNDLE", "VACANCY_EXTRACTION"], now);
            if (!DiscoveryReviewRules.ValidRecoveryContent(envelope)) throw Conflict("DISCOVERY_RECOVERY_NOT_REPRESENTABLE");
            var payload = JsonSerializer.Serialize(envelope, Json);
            db.DiscoveryCandidates.Add(new() { Id = Guid.NewGuid(), CandidateOrigin = DiscoveryRecoveryProvenance.Origin,
                RunId = id.ToString(), Query = "", PageNumber = 1, ItemIndex = o.ItemIndex, ContentStatus = "NEEDS_REVIEW", TargetStatus = "INSUFFICIENT_DATA",
                ContentGroupHash = envelope.ContentGroupHash, IdentityWarning = warnings.Length > 0, Payload = payload,
                PayloadHash = DiscoveryRecoveryProvenance.EnvelopeHash(payload), IngestedAt = now });
        }
        var execution = await db.DiscoveryExecutions.SingleAsync(x => x.Id == id, ct);
        execution.State = "PARTIAL"; execution.ErrorCode = Reason; execution.Version++; execution.UpdatedAt = now; execution.ActiveKey = null;
        var result = new Receipt(receiptId, id, request.OperationId.Value, actor, auditId, requestHash, preview.PreviewFingerprint,
            execution.Version, "PARTIAL", Reason, snapshot.Observations.Length, now);
        var resultJson = JsonSerializer.Serialize(result, Json);
        db.DiscoveryExecutionEvents.AddRange(
            new() { Id = auditId, ExecutionId = id, State = DiscoveryRecoveryProvenance.AuditState, At = now,
                Reason = JsonSerializer.Serialize(new { actor, operationId = request.OperationId, reason = request.Reason.Trim(), preview.PreviewFingerprint, receiptId }, Json) },
            new() { Id = receiptId, ExecutionId = id, State = DiscoveryRecoveryProvenance.ReceiptState, At = now, Reason = resultJson },
            new() { Id = Guid.NewGuid(), ExecutionId = id, State = Reason, At = now });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException) { throw Conflict(); }
        return JsonDocument.Parse(resultJson).RootElement.Clone();
    }

    private async Task<Snapshot> Load(Guid id, CancellationToken ct)
    {
        var e = await db.DiscoveryExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("DISCOVERY_EXECUTION_NOT_FOUND", "Execution was not found.");
        var observations = await db.DiscoveryExecutionObservations.AsNoTracking().Where(x => x.ExecutionId == id).OrderBy(x => x.ItemIndex).ThenBy(x => x.Id).Take(201).ToArrayAsync(ct);
        var rawIds = observations.Where(x => x.RawJobPostingId != null).Select(x => x.RawJobPostingId!.Value).ToArray();
        var candidates = await db.DiscoveryCandidates.AsNoTracking().Where(x => x.RunId == id.ToString()).OrderBy(x => x.Id).Take(201).ToArrayAsync(ct);
        var candidateIds = candidates.Select(x => x.Id).ToArray(); var observationIds = observations.Select(x => x.Id).ToArray();
        var vacancies = await db.DiscoveryExecutionVacancies.AsNoTracking().Where(x => observationIds.Contains(x.ObservationId)).OrderBy(x => x.Id).Take(6001).ToArrayAsync(ct);
        var raw=await db.RawJobPostings.AsNoTracking().Where(x=>rawIds.Contains(x.Id)).OrderBy(x=>x.Id).ToArrayAsync(ct);
        var jobIds = vacancies.Where(x => x.JobPostingId != null).Select(x => x.JobPostingId!.Value)
            .Concat(raw.Where(x=>x.JobPostingId!=null).Select(x=>x.JobPostingId!.Value)).Distinct().ToArray();
        return new(e, await db.DiscoveryExecutionAttempts.AsNoTracking().Where(x => x.ExecutionId == id).OrderBy(x => x.Id).Take(3).ToArrayAsync(ct), observations,
            raw,
            await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.ExecutionId == id).OrderBy(x => x.Id).Take(1001).ToArrayAsync(ct), candidates,
            await db.DiscoveryCandidateReviews.AsNoTracking().Where(x => candidateIds.Contains(x.CandidateId)).OrderBy(x => x.CandidateId).ToArrayAsync(ct),
            await db.DiscoveryCandidateReviewEvents.AsNoTracking().Where(x => candidateIds.Contains(x.CandidateId)).OrderBy(x => x.Id).Take(10001).ToArrayAsync(ct), vacancies,
            await db.JobPostings.AsNoTracking().Where(x => jobIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct));
    }

    private DiscoveryRecoveryPreview Evaluate(Snapshot s)
    {
        var blocks = new List<RecoveryBlocker>(); void Block(string code, int? index = null) { if (blocks.Count < 220) blocks.Add(new(code, index)); }
        if (!readiness.CanFinalize) Block("PRODUCTION_FINALIZE_NOT_VERIFIED");
        if (s.Execution.State != "INGESTING" || s.Execution.ActiveKey is null || s.Execution.ResultHash != null) Block("EXECUTION_NOT_RECOVERABLE");
        if (s.Observations.Length is 0 or > 200 || s.Events.Length > 1000 || s.Candidates.Length > 200 || s.ReviewEvents.Length > 10000 || s.Vacancies.Length > 6000)
            Block("RECOVERY_SNAPSHOT_BOUND_OR_INCOMPLETE");
        if (s.Candidates.Length > 0 || s.Vacancies.Length > 0) Block("EXISTING_RECOVERY_CONTENT_CONFLICT");
        var attempt = s.Attempts.Length == 1 ? s.Attempts[0] : null;
        if(attempt?.Outcome!="CLAIMED")Block("ATTEMPT_OUTCOME_CONFLICT");
        if (attempt is null || s.Execution.WorkflowRunId != attempt.WorkflowRunId || !Regex.IsMatch(attempt.WorkflowRunId, "^[1-9][0-9]{0,19}$")) Block("ATTEMPT_RUN_CONFLICT");
        var evidence = s.Events.Where(x => x.State == "PROVIDER_EVIDENCE").ToArray();
        if (evidence.Length != 1 || attempt is null) Block("PROVIDER_EVIDENCE_MISSING_OR_AMBIGUOUS");
        else {
            try {
                var recorded = JsonSerializer.Deserialize<DiscoveryExecutionResult>(evidence[0].Reason!, Json);
                if (recorded is null || !DiscoveryExecutionService.RecoveryEvidenceValid(recorded) || recorded.CreditsUsedAlternateHeader<0 || attempt.ActualRequests != 1 ||
                    attempt.CreditsDeducted != recorded.CreditsDeducted || attempt.CreditsUsedAlternateHeader != recorded.CreditsUsedAlternateHeader || attempt.HttpStatus != recorded.HttpStatus)
                    Block("PROVIDER_COST_OR_OUTCOME_UNKNOWN");
            }
            catch (Exception error) when(error is JsonException or ArgumentException) { Block("PROVIDER_EVIDENCE_INVALID"); }
        }
        for (var n = 0; n < s.Observations.Length; n++) {
            var o = s.Observations[n]; if (o.ItemIndex != n) Block("OBSERVATION_INDEX_CONFLICT", o.ItemIndex);
            if (o.SemanticState is not ("NOT_REQUESTED" or "COMPLETED")) Block("SEMANTIC_ATTEMPT_UNRESOLVED", o.ItemIndex);
            try { ValidateSource(o, s.Raw.SingleOrDefault(x => x.Id == o.RawJobPostingId), code => Block(code, o.ItemIndex)); }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or FormatException or KeyNotFoundException) { Block("OBSERVATION_PAYLOAD_INVALID", o.ItemIndex); }
        }
        // Fingerprint ALL inspected persisted dependencies, including exact bytes
        // of JSON/evidence and independent review history. Not returned as data.
        string fingerprint;
        try{
            fingerprint = DiscoveryRecoveryProvenance.Hash(new { policy = "discovery-recovery-preview-v1", envelope = DiscoveryRecoveryProvenance.Version,
                snapshot = s, blocks, productionReady = readiness.CanFinalize });
        }catch(JsonException){
            // An unrepresentable snapshot is NEVER eligible; do not emit private
            // exception text or attempt to normalize its persisted JSON.
            Block("RECOVERY_SNAPSHOT_NOT_REPRESENTABLE");
            fingerprint=DiscoveryRecoveryProvenance.Hash(new{id=s.Execution.Id,s.Execution.Version,blocked="RECOVERY_SNAPSHOT_NOT_REPRESENTABLE"});
        }
        var incomplete = blocks.Any(x => x.Code is "RECOVERY_SNAPSHOT_BOUND_OR_INCOMPLETE" or "OBSERVATION_NOT_REPRESENTABLE" or "PRODUCTION_FINALIZE_NOT_VERIFIED");
        return new(s.Execution.Id, blocks.Count == 0 ? "RECOVERABLE" : incomplete ? "INCOMPLETE" : "UNKNOWN", s.Execution.Version, s.Execution.WorkflowRunId,
            s.Observations.Length, "UNKNOWN", fingerprint, blocks, blocks.Count==0 ? ["PERSISTED_RAW_CONTENT", "PROVIDER_ACCOUNTING", "SOURCE_PROVENANCE"] : ["PERSISTED_OBSERVATIONS_RETAINED"],
            ["ORIGINAL_QUERY", "ORIGINAL_CLASSIFIER", "ORIGINAL_REVIEW_BUNDLE", "WORKER_VACANCY_EXTRACTION", "PROVIDER_RESPONSE_COMPLETENESS"]);
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static void ValidateSource(DiscoveryExecutionObservation o, RawJobPosting? source, Action<string> block)
    {
        using var doc = JsonDocument.Parse(o.PayloadJson); var item = doc.RootElement;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("itemIndex",out var index) || !index.TryGetInt32(out var itemIndex) || itemIndex != o.ItemIndex) { block("CROSS_LAYER_PROVENANCE_CONFLICT"); return; }
        foreach (var name in new[] { "providerPostId", "sourceUrl", "providerSourceUrl", "rawContent", "contentHash", "publishedAt" })
            if (item.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) block("OBSERVATION_PAYLOAD_INVALID");
        var id = Text(item, "providerPostId"); var url = Text(item, "sourceUrl"); var providerUrl = Text(item, "providerSourceUrl"); var raw = Text(item, "rawContent"); var hash = Text(item, "contentHash");
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 24000 || raw.Contains('\0')) { block("OBSERVATION_NOT_REPRESENTABLE"); return; }
        if (id is null || !Regex.IsMatch(id, "^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$")) block("SOURCE_IDENTITY_UNSUPPORTED");
        if (hash != DiscoveryReviewRules.Hash(raw) || o.ContentHash != hash) block("CORRUPTED_HASH");
        var identity = DiscoveryRecoveryProvenance.Hash(new { itemIndex = o.ItemIndex, providerPostId = id, sourceUrl = url, rawContent = raw, contentHash = hash, publishedAt = Text(item, "publishedAt") });
        if (identity != o.PayloadHash) block("CORRUPTED_SOURCE_IDENTITY_HASH");
        if (Text(item, "publishedAt") is { } at && !DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) block("PUBLICATION_DATE_INVALID");
        if (providerUrl?.Length > 2048 || providerUrl?.Contains('\0') == true) block("SOURCE_PROVENANCE_UNSAFE");
        var mismatch = Text(item, "outcome") == "POST_ID_URL_MISMATCH";
        if (mismatch) {
            if (url is not null || providerUrl is null || !DiscoveryReviewRules.SafeUrl(providerUrl) || new Uri(providerUrl).AbsolutePath.TrimEnd('/').Split('/').Last() == id)
                block("CROSS_LAYER_PROVENANCE_CONFLICT");
        }
        else if (Text(item, "outcome") != "READY_FOR_EXTRACTION" || url is null || !DiscoveryReviewRules.SafeUrl(url) || new Uri(url).AbsolutePath.TrimEnd('/').Split('/').Last() != id)
            block("SOURCE_IDENTITY_NOT_VERIFIED");
        if (source is null) { block("RAW_SOURCE_LINK_MISSING"); return; }
        if(source.Metadata is null||source.Metadata.RootElement.ValueKind!=JsonValueKind.Object){block("CROSS_LAYER_PROVENANCE_CONFLICT");return;}
        var metadata = source.Metadata.RootElement;
        foreach(var field in new[]{"providerPostId","sourceUrl","providerSourceUrl","exactRawContentHash"})
            if(!metadata.TryGetProperty(field,out var entry)||entry.ValueKind is not(JsonValueKind.String or JsonValueKind.Null))block("CROSS_LAYER_PROVENANCE_CONFLICT");
        if (source.Source != "FACEBOOK" || source.RawContent != raw || source.ContentHash != JobHashing.Sha256(JobHashing.NormalizeText(raw)) ||
            source.SourceExternalId is not null && source.SourceExternalId != id ||
            Text(metadata, "providerPostId") != id || Text(metadata, "sourceUrl") != url || Text(metadata, "providerSourceUrl") != providerUrl ||
            Text(metadata, "exactRawContentHash") != hash || source.IngestionKey != "discovery:" + DiscoveryReviewRules.Hash((id ?? "unknown") + "\n" + hash))
            block("CROSS_LAYER_PROVENANCE_CONFLICT");
        if (source.SourceUrl is { } storedUrl && storedUrl != url) block("CROSS_LAYER_PROVENANCE_CONFLICT");
    }
}
