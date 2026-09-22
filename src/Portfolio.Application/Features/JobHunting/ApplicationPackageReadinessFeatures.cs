using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public static class ApplicationPackageReadinessStatuses
{
    public const string ReadyToFinalize = "READY_TO_FINALIZE";
    public const string NotReady = "NOT_READY";
}

public static class ApplicationPackageReadinessBlockerCodes
{
    public const string ApplicationNotDraft = "APPLICATION_NOT_DRAFT";
    public const string JobNotFound = "JOB_NOT_FOUND";
    public const string JobNotApproved = "JOB_NOT_APPROVED";
    public const string JobArchived = "JOB_ARCHIVED";
    public const string CanonicalCvMissing = "CANONICAL_CV_MISSING";
    public const string CanonicalCvInvalid = "CANONICAL_CV_INVALID";
}

public sealed record ApplicationPackageReadinessBlocker(string Code, string Message);
public sealed record ApplicationPackageCanonicalCv(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string ContentHash,
    int Version,
    DateTimeOffset UpdatedAt);
public sealed record ApplicationPackageReadinessResult(
    string Status,
    bool IsReady,
    IReadOnlyCollection<ApplicationPackageReadinessBlocker> Blockers,
    int ApplicationVersion,
    int? JobPostingVersion,
    int? CanonicalCvVersion,
    ApplicationPackageCanonicalCv? CanonicalCv);

public sealed record GetApplicationPackageReadinessQuery(Guid ApplicationId)
    : IRequest<ApplicationPackageReadinessResult>;

public sealed class ApplicationPackageReadinessEvaluator
{
    private const long MaximumCvBytes = 10 * 1024 * 1024;

    public ApplicationPackageReadinessResult Evaluate(
        JobApplication application,
        JobPosting? jobPosting,
        CanonicalCv? canonicalCv)
    {
        var blockers = new List<ApplicationPackageReadinessBlocker>();

        if (application.Status != JobApplicationStatuses.Draft)
            blockers.Add(new(ApplicationPackageReadinessBlockerCodes.ApplicationNotDraft, "The application must be in DRAFT status."));

        if (jobPosting is null)
        {
            blockers.Add(new(ApplicationPackageReadinessBlockerCodes.JobNotFound, "The associated job posting was not found."));
        }
        else
        {
            if (jobPosting.SelectionStatus != JobPostingSelectionStatuses.Approved)
                blockers.Add(new(ApplicationPackageReadinessBlockerCodes.JobNotApproved, "The associated job posting must be approved."));
            if (jobPosting.ArchivedAt is not null)
                blockers.Add(new(ApplicationPackageReadinessBlockerCodes.JobArchived, "The associated job posting is archived."));
        }

        if (canonicalCv is null)
        {
            blockers.Add(new(ApplicationPackageReadinessBlockerCodes.CanonicalCvMissing, "A canonical CV has not been configured."));
        }
        else if (!IsValid(canonicalCv))
        {
            blockers.Add(new(ApplicationPackageReadinessBlockerCodes.CanonicalCvInvalid, "The canonical CV metadata is invalid."));
        }

        var cv = canonicalCv is null
            ? null
            : new ApplicationPackageCanonicalCv(
                canonicalCv.FileName,
                canonicalCv.ContentType,
                canonicalCv.FileSizeBytes,
                canonicalCv.ContentHash,
                canonicalCv.Version,
                canonicalCv.UpdatedAt);
        var ready = blockers.Count == 0;
        return new(
            ready ? ApplicationPackageReadinessStatuses.ReadyToFinalize : ApplicationPackageReadinessStatuses.NotReady,
            ready,
            blockers,
            application.Version,
            jobPosting?.Version,
            canonicalCv?.Version,
            cv);
    }

    private static bool IsValid(CanonicalCv cv) =>
        !string.IsNullOrWhiteSpace(cv.StorageKey) &&
        !string.IsNullOrWhiteSpace(cv.FileName) &&
        cv.ContentType == "application/pdf" &&
        cv.FileSizeBytes is >= 1 and <= MaximumCvBytes &&
        cv.Version >= 1 &&
        !string.IsNullOrEmpty(cv.ContentHash) &&
        cv.ContentHash.Length == 64 &&
        cv.ContentHash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class GetApplicationPackageReadinessQueryHandler(
    IApplicationDbContext db,
    ApplicationPackageReadinessEvaluator evaluator)
    : IRequestHandler<GetApplicationPackageReadinessQuery, ApplicationPackageReadinessResult>
{
    public async Task<ApplicationPackageReadinessResult> HandleAsync(
        GetApplicationPackageReadinessQuery request,
        CancellationToken cancellationToken = default)
    {
        var inputs = await (
            from application in db.JobApplications.AsNoTracking()
            where application.Id == request.ApplicationId
            join jobPosting in db.JobPostings.AsNoTracking()
                on application.JobPostingId equals jobPosting.Id into jobPostings
            from jobPosting in jobPostings.DefaultIfEmpty()
            from canonicalCv in db.CanonicalCvs.AsNoTracking().DefaultIfEmpty()
            select new { Application = application, JobPosting = jobPosting, CanonicalCv = canonicalCv })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");

        return evaluator.Evaluate(inputs.Application, inputs.JobPosting, inputs.CanonicalCv);
    }
}
