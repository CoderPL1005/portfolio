using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;

namespace Portfolio.Application.Features.JobHunting;

public static class EmailApplicationWorkflowStatuses
{
    public const string Blocked = "BLOCKED";
    public const string Created = "CREATED";
    public const string Approved = "APPROVED";
    public const string Submitting = "SUBMITTING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
}

public sealed record SubmitFacebookEmailApplicationCommand(Guid JobPostingId, Guid ClientRequestId)
    : IRequest<EmailApplicationWorkflowResult>;

public sealed record CreateAuthorizedEmailSubmissionAttemptCommand(CreateSubmissionAttemptCommand Command)
    : IRequest<SubmissionAttemptResult>;
public sealed record ApproveAuthorizedEmailSubmissionAttemptCommand(Guid AttemptId, int ExpectedVersion)
    : IRequest<SubmissionAttemptResult>;
public sealed record ExecuteAuthorizedEmailSubmissionAttemptCommand(Guid AttemptId, int ExpectedVersion)
    : IRequest<SubmissionAttemptResult>;

public sealed class CreateAuthorizedEmailSubmissionAttemptCommandHandler(CreateSubmissionAttemptCommandHandler handler)
    : IRequestHandler<CreateAuthorizedEmailSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public Task<SubmissionAttemptResult> HandleAsync(CreateAuthorizedEmailSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        handler.HandleAuthorizedEmailAsync(request.Command, cancellationToken);
}

public sealed class ApproveAuthorizedEmailSubmissionAttemptCommandHandler(ApproveSubmissionAttemptCommandHandler handler)
    : IRequestHandler<ApproveAuthorizedEmailSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public Task<SubmissionAttemptResult> HandleAsync(ApproveAuthorizedEmailSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        handler.HandleAuthorizedEmailAsync(new(request.AttemptId, request.ExpectedVersion), cancellationToken);
}

public sealed class ExecuteAuthorizedEmailSubmissionAttemptCommandHandler(ExecuteSubmissionAttemptCommandHandler handler)
    : IRequestHandler<ExecuteAuthorizedEmailSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public Task<SubmissionAttemptResult> HandleAsync(ExecuteAuthorizedEmailSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        handler.HandleAuthorizedEmailAsync(new(request.AttemptId, request.ExpectedVersion), cancellationToken);
}

public sealed record EmailApplicationWorkflowResult(
    Guid JobPostingId,
    Guid? JobApplicationId,
    string Status,
    string? BlockerCode,
    string? Message,
    int? OverallScore,
    int CoveragePercent,
    string? ApplicationEmail,
    string? PackageStatus,
    int? PackageRevision,
    SubmissionAttemptResult? Attempt);

public sealed class SubmitFacebookEmailApplicationCommandHandler(
    IApplicationDbContext db,
    IRequestDispatcher dispatcher,
    IApplicationEmailSender emailSender,
    IAdminJobNotificationSender notifications,
    IOptions<JobRecommendationOptions> recommendationOptions)
    : IRequestHandler<SubmitFacebookEmailApplicationCommand, EmailApplicationWorkflowResult>
{
    public async Task<EmailApplicationWorkflowResult> HandleAsync(
        SubmitFacebookEmailApplicationCommand request,
        CancellationToken cancellationToken = default)
    {
        var job = await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == request.JobPostingId, cancellationToken)
            ?? throw new NotFoundException("JOB_POSTING_NOT_FOUND", "The job posting was not found.");
        var manualSource = await db.RawJobPostings.AsNoTracking().AnyAsync(item =>
            item.JobPostingId == job.Id &&
            (item.Source == RawJobPostingSources.Facebook || item.Source == RawJobPostingSources.Manual),
            cancellationToken);
        if (!manualSource)
            throw new ConflictException("EMAIL_APPLICATION_SOURCE_NOT_SUPPORTED",
                "Only Facebook or manual screenshot jobs can use this email workflow.");

        var application = await db.JobApplications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.JobPostingId == job.Id, cancellationToken);
        var existing = application is { PackageStatus: JobApplicationPackageStatuses.Finalized, PackageRevision: 1,
            PackageManifestHash: not null }
            ? await ExistingAttemptAsync(application, request.ClientRequestId, cancellationToken)
            : null;
        if (existing is not null && existing.Status is SubmissionAttemptStatuses.Submitting
            or SubmissionAttemptStatuses.Succeeded or SubmissionAttemptStatuses.Failed or SubmissionAttemptStatuses.Unknown)
            return Result(job, application, existing.Status, null, ExistingMessage(existing), null, existing);

        var fit = await dispatcher.DispatchAsync(new GetJobFitAnalysisQuery(job.Id), cancellationToken);
        var thresholds = recommendationOptions.Value;
        if (!fit.OverallScore.HasValue || fit.OverallScore < thresholds.RecommendedMinimumScore
            || fit.CoveragePercent < thresholds.RecommendedMinimumCoverage)
        {
            var reasons = fit.Concerns.Take(3).ToArray();
            var message = reasons.Length == 0
                ? "This job did not pass the configured fit safety gate."
                : "This job did not pass the fit safety gate: " + string.Join(" ", reasons);
            await NotifySafelyAsync("FIT_GATE_BLOCKED", message, job.Id, cancellationToken);
            return Result(job, application, EmailApplicationWorkflowStatuses.Blocked,
                "FIT_GATE_BLOCKED", message, fit, null);
        }

        var recipient = ValidEmail(job.ApplicationEmail);
        if (recipient is null)
        {
            const string message = "No application email was found in this JD. Manual application may be required.";
            await NotifySafelyAsync("APPLICATION_EMAIL_MISSING", message, job.Id, cancellationToken);
            return Result(job, application, EmailApplicationWorkflowStatuses.Blocked,
                "APPLICATION_EMAIL_MISSING", message, fit, null);
        }

        emailSender.EnsureCanSend(recipient); // Test-mode/disabled checks occur before any mutation or network call.

        if (job.ArchivedAt is not null)
            throw new ConflictException("JOB_POSTING_ARCHIVED", "An application cannot be submitted for an archived job posting.");
        if (job.SelectionStatus != JobPostingSelectionStatuses.Approved)
        {
            if (!JobSelectionTransitionPolicy.CanTransition(job.SelectionStatus, JobPostingSelectionStatuses.Approved))
                throw new ConflictException("JOB_POSTING_SELECTION_CONFLICT", "The job cannot be approved for application.");
            var approved = await dispatcher.DispatchAsync(
                new UpdateJobSelectionCommand(job.Id, JobPostingSelectionStatuses.Approved, job.Version), cancellationToken);
            job = await db.JobPostings.AsNoTracking().SingleAsync(item => item.Id == approved.Id, cancellationToken);
        }

        application ??= await CreateApplicationAsync(job, cancellationToken);
        if (application.Status != JobApplicationStatuses.Draft)
            throw new ConflictException("JOB_APPLICATION_NOT_DRAFT", "Only a DRAFT application can be submitted.");
        if (!string.Equals(application.ApplicationEmail, recipient, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("APPLICATION_EMAIL_MISMATCH",
                "The application email no longer matches the authoritative job posting.");

        if (application.PackageStatus != JobApplicationPackageStatuses.Finalized)
        {
            if (!string.Equals(application.Channel, JobApplicationChannels.Email, StringComparison.Ordinal))
            {
                var updated = await dispatcher.DispatchAsync(new UpdateJobApplicationCommand(
                    application.Id, application.Version, JobApplicationChannels.Email,
                    application.ApplicationEmail, application.ApplicationUrl,
                    application.ExternalApplicationId, application.Notes), cancellationToken);
                application = await db.JobApplications.AsNoTracking().SingleAsync(item => item.Id == updated.Id, cancellationToken);
            }
            var readiness = await dispatcher.DispatchAsync(
                new GetApplicationPackageReadinessQuery(application.Id), cancellationToken);
            if (!readiness.IsReady)
            {
                var blocker = readiness.Blockers.First();
                throw new ConflictException(blocker.Code, blocker.Message);
            }
            await dispatcher.DispatchAsync(new FinalizeApplicationPackageCommand(
                application.Id, readiness.ApplicationVersion, readiness.JobPostingVersion!.Value,
                readiness.CanonicalCvVersion!.Value), cancellationToken);
            application = await db.JobApplications.AsNoTracking().SingleAsync(item => item.Id == application.Id, cancellationToken);
        }

        var authoritativeJob = await db.JobPostings.AsNoTracking().SingleAsync(item => item.Id == job.Id, cancellationToken);
        if (authoritativeJob.Version != job.Version
            || !string.Equals(authoritativeJob.ApplicationEmail, recipient, StringComparison.OrdinalIgnoreCase)
            || application.PackageJobPostingVersion != authoritativeJob.Version)
            throw new ConflictException("EMAIL_AUTHORITATIVE_DATA_CHANGED",
                "The authoritative job or finalized application package changed before sending.");
        job = authoritativeJob;

        existing ??= await ExistingAttemptAsync(application, request.ClientRequestId, cancellationToken);
        var attempt = existing ?? await dispatcher.DispatchAsync(new CreateAuthorizedEmailSubmissionAttemptCommand(
            new(application.Id, SubmissionProviders.Email, request.ClientRequestId, application.Version,
                application.PackageRevision, application.PackageManifestHash!)), cancellationToken);
        if (attempt.Status == SubmissionAttemptStatuses.Created)
            attempt = await dispatcher.DispatchAsync(new ApproveAuthorizedEmailSubmissionAttemptCommand(attempt.Id, attempt.Version), cancellationToken);
        if (attempt.Status == SubmissionAttemptStatuses.Approved)
            attempt = await dispatcher.DispatchAsync(new ExecuteAuthorizedEmailSubmissionAttemptCommand(attempt.Id, attempt.Version), cancellationToken);

        var notification = attempt.Status == SubmissionAttemptStatuses.Succeeded
            ? ("APPLICATION_EMAIL_SENT", "Application email sent successfully.")
            : ("APPLICATION_EMAIL_SEND_FAILED", attempt.Status == SubmissionAttemptStatuses.Unknown
                ? "The email outcome is uncertain and requires manual reconciliation."
                : "The application email was not sent.");
        await NotifySafelyAsync(notification.Item1, notification.Item2, job.Id, CancellationToken.None);
        return Result(job, application, attempt.Status, attempt.FailureCode,
            attempt.FailureMessage ?? notification.Item2, fit, attempt);
    }

    private async Task<Portfolio.Domain.Entities.JobApplication> CreateApplicationAsync(
        Portfolio.Domain.Entities.JobPosting job, CancellationToken cancellationToken)
    {
        var created = await dispatcher.DispatchAsync(new CreateJobApplicationCommand(job.Id, job.Version), cancellationToken);
        return await db.JobApplications.AsNoTracking().SingleAsync(item => item.Id == created.Id, cancellationToken);
    }

    private async Task<SubmissionAttemptResult?> ExistingAttemptAsync(
        Portfolio.Domain.Entities.JobApplication application, Guid clientRequestId, CancellationToken cancellationToken)
    {
        var key = SubmissionAttemptIdempotency.Create(application.Id, application.PackageRevision,
            application.PackageManifestHash!, SubmissionProviders.Email, clientRequestId);
        var id = await db.SubmissionAttempts.AsNoTracking().Where(item => item.IdempotencyKey == key)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await SubmissionAttemptMapping.GetAsync(db, id.Value, cancellationToken);
    }

    private async Task NotifySafelyAsync(string code, string message, Guid jobPostingId, CancellationToken cancellationToken)
    {
        try { await notifications.SendAsync(code, message, jobPostingId, cancellationToken); }
        catch { /* Push delivery is secondary to the authoritative workflow result. */ }
    }

    private static string? ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value.Trim(), out var parsed)
        && string.Equals(parsed.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
            ? value.Trim() : null;
    private static string ExistingMessage(SubmissionAttemptResult attempt) => attempt.Status switch
    {
        SubmissionAttemptStatuses.Succeeded => "Application email was already sent.",
        SubmissionAttemptStatuses.Submitting => "Application email execution is already in progress or requires reconciliation.",
        SubmissionAttemptStatuses.Unknown => "The previous email outcome is uncertain and requires reconciliation.",
        _ => "The previous email attempt failed and was not retried automatically.",
    };
    private static EmailApplicationWorkflowResult Result(
        Portfolio.Domain.Entities.JobPosting job, Portfolio.Domain.Entities.JobApplication? application, string status,
        string? blockerCode, string? message, JobFitAnalysisResult? fit, SubmissionAttemptResult? attempt) =>
        new(job.Id, application?.Id, status, blockerCode, message, fit?.OverallScore,
            fit?.CoveragePercent ?? 0, job.ApplicationEmail, application?.PackageStatus,
            application?.PackageRevision, attempt);
}

public sealed class SubmitFacebookEmailApplicationCommandValidator
    : IRequestValidator<SubmitFacebookEmailApplicationCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        SubmitFacebookEmailApplicationCommand request, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        if (request.JobPostingId == Guid.Empty) failures.Add(new("jobPostingId", "Job posting ID is required."));
        if (request.ClientRequestId == Guid.Empty) failures.Add(new("clientRequestId", "Client request ID is required."));
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}
