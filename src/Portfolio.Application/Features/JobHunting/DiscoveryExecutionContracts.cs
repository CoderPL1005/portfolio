using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Application.Common.Models;

namespace Portfolio.Application.Features.JobHunting;

public sealed class DiscoveryExecutionOptions
{
    public bool Enabled { get; init; }
    public bool WorkerEnabled { get; init; }
    public string WorkerKey { get; init; } = "";
    public string GitHubToken { get; init; } = "";
    public string Repository { get; init; } = "CoderPL1005/portfolio";
    public string Workflow { get; init; } = "job-discovery-ingestion.yml";
    public bool FreePlanConfirmed { get; init; }
    public bool NoCashConfirmed { get; init; }
    public bool SemanticEnabled { get; init; }
    public bool GeminiFreeTierConfirmed { get; init; }
    public int MaximumSemanticAttempts { get; init; } = 3;
    public string[] Seniorities { get; init; } = ["Intern", "Fresher"];
    public string PrimaryRole { get; init; } = "BACKEND";
    public bool AllowFullStack { get; init; } = true;
    public int PreferredExperienceMax { get; init; } = 1;
    public int FallbackExperienceMax { get; init; } = 2;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartDiscoveryExecutionRequest(Guid ClientRequestId);
public sealed record DiscoveryExecutionResult(int ActualRequests, decimal? CreditsDeducted,
    decimal? CreditsUsedAlternateHeader, int? HttpStatus, JsonElement[] Observations,
    string? State = null, string? ErrorCode = null, string? TriggerSource = null, DiscoveryReviewImport? ReviewBundle = null,
    string? ProviderOperation = null, DiscoveryUsageEvidence? UsageEvidence = null);
public sealed record DiscoveryUsageEvidence(bool BodyCostPresent, JsonElement BodyCreditsCharged,
    string? DeductedHeader = null,string? AlternateHeader = null);
public sealed record DiscoverySemanticRequest(int ItemIndex, string RawContent, string ContentHash);
public sealed record DiscoveryProcessingRequest(int ItemIndex, string? ContentHash, DiscoveryReviewItem? Review,
    JsonElement[] Vacancies, string? ExtractionError = null, int? ExpectedVersion = null, string? ExpectedWorkflowRunId = null);
public sealed record DiscoveryPersistedSemanticRequest(int ItemIndex, string RawContent, string ContentHash,
    int ExpectedVersion, string ExpectedWorkflowRunId, bool ConfirmGeminiFreeTier = false);
public sealed record DiscoveryTextGeneration(string ModelIdentity, string CacheVersion, string Output, int? InputTokens, int? OutputTokens);
public interface IDiscoveryTextExtractor
{
    string ModelIdentity { get; }
    Task<DiscoveryTextGeneration> ExtractAsync(string rawContent, CancellationToken ct);
}
public interface IDiscoveryGitHubClient
{
    Task<string> DispatchAsync(Guid id, CancellationToken ct);
    Task<bool> VerifyRunAsync(Guid id, string runId, CancellationToken ct);
}
public interface IDiscoveryExecutionService
{
    Task<object> StartAsync(Guid clientRequestId, string triggerSource, CancellationToken ct = default);
    Task<object> GetAsync(Guid id, CancellationToken ct = default);
    Task<object> HistoryAsync(int page, int pageSize, CancellationToken ct = default);
    Task<object> ConfigurationAsync(Guid id, string runId, CancellationToken ct);
    Task<object> ClaimAsync(Guid id, string runId, CancellationToken ct);
    Task<object> ObservationsAsync(Guid id, string runId, DiscoveryExecutionResult result, CancellationToken ct);
    Task<object> CompleteAsync(Guid id, string runId, DiscoveryExecutionResult result, CancellationToken ct);
    Task<DiscoveryTextGeneration> ExtractAsync(Guid id, string runId, DiscoverySemanticRequest request, CancellationToken ct);
    Task<object> ProcessAsync(Guid id, DiscoveryProcessingRequest request, string? workerRunId, CancellationToken ct);
    Task<object> ExtractPersistedAsync(Guid id, DiscoveryPersistedSemanticRequest request, CancellationToken ct);
}
