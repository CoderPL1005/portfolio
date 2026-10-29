using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscoveryRecoveryFinalize(Guid? OperationId, int? ExpectedVersion, string? ExpectedWorkflowRunId,
    string? PreviewFingerprint, string? Confirmation, string? Reason);
public sealed class DiscoveryRecoveryOptions
{
    // Release prerequisites; never enabled by running unit tests or by a request.
    public bool ProductionFinalizeEnabled { get; init; }
    public bool ProjectionReady { get; init; }
    public string? PostgreSqlVerificationReference { get; init; }
}
public sealed record RecoveryDecision(string ContentStatus, string TargetStatus, string VerificationStatus);
public sealed record DiscoveryRecoveryEnvelope(string Origin, string Version, Guid ExecutionId, Guid ObservationId,
    string WorkflowRunId, Guid OperationId, Guid ReceiptId, Guid AuditId, int ItemIndex, string SourceFingerprint,
    string? OriginalQuery, object? OriginalClassifierResult, object? OriginalReviewBundle,
    string ProviderPostId, string? SourceUrl, string? ProviderSourceUrl, string RawContent, string ContentHash,
    string ContentGroupHash, DateTimeOffset? PublishedAt, RecoveryDecision RecoveryDecision,
    string[] SourceWarnings, string[] MissingFields, DateTimeOffset RecoveryAt);
public sealed record RecoveryBlocker(string Code, int? ItemIndex = null);
public sealed record DiscoveryRecoveryPreview(Guid ExecutionId, string Status, int Version, string? WorkflowRunId,
    int ObservationCount, string ProviderResponseCompleteness, string PreviewFingerprint,
    IReadOnlyList<RecoveryBlocker> Blockers, string[] RecoverableFields, string[] MissingFields);
public interface IDiscoveryExecutionRecovery
{
    Task<DiscoveryRecoveryPreview> PreviewAsync(Guid id, CancellationToken ct);
    Task<JsonElement> FinalizeAsync(Guid id, DiscoveryRecoveryFinalize request, CancellationToken ct);
}
public interface IDiscoveryRecoveryTransactions
{
    Task<IDiscoveryRecoveryTransaction> BeginAsync(bool write, CancellationToken ct);
}
public interface IDiscoveryRecoveryReadiness { bool CanFinalize { get; } }
public interface IDiscoveryRecoveryTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public static class DiscoveryRecoveryProvenance
{
    public const string Normal = "CLASSIFIER";
    public const string Origin = "PERSISTED_OBSERVATION_RECOVERY";
    public const string Version = "recovery-review-v1";
    public const string ReceiptState = "RECOVERY_RECEIPT";
    public const string AuditState = "RECOVERY_AUDIT";
    public static string Canonical(JsonElement e) => e.ValueKind switch {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Canonical)) + "]", _ => e.GetRawText()
    };
    public static string Hash(object value) => DiscoveryReviewRules.Hash(Canonical(JsonSerializer.SerializeToElement(value, DiscoveryReviewRules.Json)));
    public static string EnvelopeHash(string payload) => DiscoveryReviewRules.Hash(Version + "\n" + payload);
    public static DiscoveryRecoveryEnvelope Read(DiscoveryCandidate candidate)
    {
        try {
            var e = JsonSerializer.Deserialize<DiscoveryRecoveryEnvelope>(candidate.Payload, DiscoveryReviewRules.Json);
            if (e is null || !DiscoveryReviewRules.ValidRecoveryContent(e) || candidate.CandidateOrigin != Origin || candidate.Query != "" || e.Origin != Origin || e.Version != Version ||
                candidate.RunId != e.ExecutionId.ToString() || candidate.PageNumber != 1 || candidate.ItemIndex != e.ItemIndex ||
                candidate.ContentStatus != "NEEDS_REVIEW" || candidate.TargetStatus != "INSUFFICIENT_DATA" ||
                candidate.ContentGroupHash != e.ContentGroupHash || candidate.IdentityWarning != e.SourceWarnings.Contains("POST_ID_URL_MISMATCH") ||
                candidate.PayloadHash != EnvelopeHash(candidate.Payload) || !DiscoveryReviewRules.ValidRecoveryContent(e)) throw Invalid();
            return e;
        }
        catch (Exception error) when(error is JsonException or ArgumentException or InvalidOperationException) { throw Invalid(); }
    }
    public static Portfolio.Application.Common.Exceptions.ConflictException Invalid() => new("DISCOVERY_RECOVERY_PROVENANCE_INVALID", "Recovery provenance is unavailable or inconsistent.");
}
