using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public static class ApplicationSubmissionReadinessStatuses
{
    public const string Ready = "READY_FOR_SUBMISSION";
    public const string NotReady = "NOT_READY_FOR_SUBMISSION";
}

public static class ApplicationSubmissionReadinessBlockerCodes
{
    public const string ApplicationNotDraft = "APPLICATION_NOT_DRAFT";
    public const string PackageNotFinalized = "PACKAGE_NOT_FINALIZED";
    public const string PackageInvalid = "PACKAGE_METADATA_INVALID";
    public const string SnapshotMissing = "PACKAGE_CV_MISSING";
    public const string SnapshotInvalid = "PACKAGE_CV_INVALID";
}

public sealed record ApplicationSubmissionReadinessBlocker(string Code, string Message);

public sealed record FinalizedApplicationPackageHandoff(
    Guid ApplicationId,
    Guid JobPostingId,
    int ApplicationVersion,
    int PackageRevision,
    string PackageManifestHash,
    Guid CvDocumentId,
    string CvFileName,
    string CvContentType,
    long CvFileSizeBytes,
    string CvContentHash);

public sealed record ApplicationSubmissionReadinessResult(
    string Status,
    bool IsReady,
    IReadOnlyCollection<ApplicationSubmissionReadinessBlocker> Blockers,
    FinalizedApplicationPackageHandoff? Package);

public sealed record GetApplicationSubmissionReadinessQuery(Guid ApplicationId)
    : IRequest<ApplicationSubmissionReadinessResult>;

public sealed class ApplicationSubmissionReadinessEvaluator
{
    private const long MaximumCvBytes = 10 * 1024 * 1024;

    public ApplicationSubmissionReadinessResult Evaluate(
        JobApplication application,
        JobApplicationDocument? managedCv)
    {
        var blockers = new List<ApplicationSubmissionReadinessBlocker>();

        if (application.Status != JobApplicationStatuses.Draft)
            blockers.Add(new(ApplicationSubmissionReadinessBlockerCodes.ApplicationNotDraft,
                "Only a DRAFT application can be prepared for a new submission."));

        if (application.PackageStatus != JobApplicationPackageStatuses.Finalized)
        {
            blockers.Add(new(ApplicationSubmissionReadinessBlockerCodes.PackageNotFinalized,
                "Finalize the immutable application package before recording a submission."));
        }
        else
        {
            if (!IsPackageValid(application))
                blockers.Add(new(ApplicationSubmissionReadinessBlockerCodes.PackageInvalid,
                    "The finalized package metadata is incomplete or invalid."));
            if (managedCv is null)
                blockers.Add(new(ApplicationSubmissionReadinessBlockerCodes.SnapshotMissing,
                    "The finalized package CV snapshot is missing."));
            else if (!IsSnapshotValid(application, managedCv))
                blockers.Add(new(ApplicationSubmissionReadinessBlockerCodes.SnapshotInvalid,
                    "The finalized package CV snapshot metadata is invalid."));
        }

        var ready = blockers.Count == 0;
        var package = ready
            ? new FinalizedApplicationPackageHandoff(
                application.Id,
                application.JobPostingId,
                application.Version,
                application.PackageRevision,
                application.PackageManifestHash!,
                managedCv!.Id,
                managedCv.FileName!,
                managedCv.ContentType!,
                managedCv.FileSizeBytes!.Value,
                managedCv.ContentHash!)
            : null;

        return new(
            ready ? ApplicationSubmissionReadinessStatuses.Ready : ApplicationSubmissionReadinessStatuses.NotReady,
            ready,
            blockers,
            package);
    }

    private static bool IsPackageValid(JobApplication application) =>
        application.PackageRevision == 1 &&
        application.PackageJobPostingVersion is >= 1 &&
        application.PackageFinalizedAt is not null &&
        application.PackageFinalizedByAdminUserId is not null &&
        IsLowercaseSha256(application.PackageManifestHash);

    private static bool IsSnapshotValid(JobApplication application, JobApplicationDocument document) =>
        document.Id != Guid.Empty &&
        document.JobApplicationId == application.Id &&
        document.DocumentType == "CV" &&
        document.PackageRevision == 1 &&
        document.RemovedAt is null &&
        !string.IsNullOrWhiteSpace(document.FileName) &&
        document.ContentType == "application/pdf" &&
        document.FileSizeBytes is >= 1 and <= MaximumCvBytes &&
        document.SourceCanonicalCvVersion is >= 1 &&
        IsLowercaseSha256(document.ContentHash);

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class GetApplicationSubmissionReadinessQueryHandler(
    IApplicationDbContext db,
    ApplicationSubmissionReadinessEvaluator evaluator)
    : IRequestHandler<GetApplicationSubmissionReadinessQuery, ApplicationSubmissionReadinessResult>
{
    public async Task<ApplicationSubmissionReadinessResult> HandleAsync(
        GetApplicationSubmissionReadinessQuery request,
        CancellationToken cancellationToken = default)
    {
        var application = await db.JobApplications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");
        var managedCv = application.PackageStatus == JobApplicationPackageStatuses.Finalized
            ? await db.JobApplicationDocuments.AsNoTracking().SingleOrDefaultAsync(
                item => item.JobApplicationId == application.Id &&
                    item.DocumentType == "CV" &&
                    item.PackageRevision == 1 &&
                    item.RemovedAt == null,
                cancellationToken)
            : null;

        return evaluator.Evaluate(application, managedCv);
    }
}
