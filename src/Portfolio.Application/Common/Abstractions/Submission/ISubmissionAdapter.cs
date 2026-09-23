namespace Portfolio.Application.Common.Abstractions.Submission;

public sealed record SubmissionAdapterCapabilities(
    bool SupportsDocumentAttachment,
    bool RequiresApplicationUrl,
    bool RequiresApplicationEmail,
    bool RequiresBrowserSession,
    bool SupportsDeterministicProviderSubmissionId);

public sealed record SubmissionRequest(
    Guid SubmissionAttemptId,
    Guid ApplicationId,
    Guid JobPostingId,
    string Provider,
    int PackageRevision,
    string PackageManifestHash,
    Guid CvDocumentId,
    string CvFileName,
    string CvContentType,
    long CvFileSizeBytes,
    string CvContentHash,
    string? ApplicationEmail,
    string? ApplicationUrl);

public sealed record SubmissionResult(
    string Outcome,
    string? ProviderSubmissionId,
    string? FailureCode,
    string? DiagnosticMessage);

public interface ISubmissionAdapter
{
    string Provider { get; }
    SubmissionAdapterCapabilities Capabilities { get; }
    bool Supports(SubmissionRequest request);
    Task<SubmissionResult> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default);
}
