using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record ApproveSubmissionAttemptCommand(Guid AttemptId, int ExpectedVersion)
    : IRequest<SubmissionAttemptResult>;
public sealed record ExecuteSubmissionAttemptCommand(Guid AttemptId, int ExpectedVersion)
    : IRequest<SubmissionAttemptResult>;

public sealed class ApproveSubmissionAttemptCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<ApproveSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public async Task<SubmissionAttemptResult> HandleAsync(ApproveSubmissionAttemptCommand request, CancellationToken cancellationToken = default)
    {
        var adminUserId = currentUser.AdminUserId
            ?? throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        var attempt = await db.SubmissionAttempts.SingleOrDefaultAsync(item => item.Id == request.AttemptId, cancellationToken)
            ?? throw SubmissionExecutionRules.NotFound();
        SubmissionExecutionRules.RequireVersion(attempt, request.ExpectedVersion);
        SubmissionExecutionRules.RequireState(attempt, SubmissionAttemptStatuses.Created);

        var now = await SubmissionExecutionRules.NextEventTimeAsync(db, attempt.Id, clock.GetUtcNow(), cancellationToken);
        SubmissionExecutionRules.Transition(db, attempt, SubmissionAttemptStatuses.Approved, adminUserId, now);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw SubmissionExecutionRules.VersionConflict(); }
        return await SubmissionAttemptMapping.GetAsync(db, attempt.Id, cancellationToken);
    }
}

public sealed class ExecuteSubmissionAttemptCommandHandler(
    IApplicationDbContext db,
    ISubmissionAttemptExecutionTransactionFactory transactionFactory,
    IEnumerable<ISubmissionAdapter> adapters,
    ApplicationSubmissionReadinessEvaluator readinessEvaluator,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<ExecuteSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public async Task<SubmissionAttemptResult> HandleAsync(ExecuteSubmissionAttemptCommand request, CancellationToken cancellationToken = default)
    {
        var adminUserId = currentUser.AdminUserId
            ?? throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        SubmissionRequest submissionRequest;
        ISubmissionAdapter adapter;
        int claimedVersion;

        await using (var transaction = await transactionFactory.BeginAsync(request.AttemptId, cancellationToken))
        {
            var attempt = transaction.Attempt ?? throw SubmissionExecutionRules.NotFound();
            SubmissionExecutionRules.RequireVersion(attempt, request.ExpectedVersion);
            SubmissionExecutionRules.RequireState(attempt, SubmissionAttemptStatuses.Approved);
            var application = transaction.Application
                ?? throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");
            var package = SubmissionExecutionRules.RequireCurrentPackage(
                application, transaction.ManagedCv, attempt, readinessEvaluator);
            if (await db.SubmissionAttempts.AsNoTracking().AnyAsync(other =>
                    other.Id != attempt.Id && other.JobApplicationId == attempt.JobApplicationId &&
                    other.PackageRevision == attempt.PackageRevision && other.Provider == attempt.Provider &&
                    (other.Status == SubmissionAttemptStatuses.Succeeded || other.Status == SubmissionAttemptStatuses.Unknown),
                    cancellationToken))
                throw new ConflictException("SUBMISSION_CONTEXT_ALREADY_RESOLVED",
                    "A successful or uncertain submission already exists for this package and provider.");

            adapter = ResolveAdapter(attempt.Provider);
            submissionRequest = new(
                attempt.Id, application.Id, application.JobPostingId, attempt.Provider,
                attempt.PackageRevision, attempt.PackageManifestHash, package.CvDocumentId,
                package.CvFileName, package.CvContentType, package.CvFileSizeBytes,
                package.CvContentHash, application.ApplicationEmail, application.ApplicationUrl);
            if (!adapter.Supports(submissionRequest))
                throw new ConflictException("SUBMISSION_ADAPTER_UNSUPPORTED",
                    "The configured adapter cannot execute this submission request.");

            var now = await SubmissionExecutionRules.NextEventTimeAsync(db, attempt.Id, clock.GetUtcNow(), cancellationToken);
            attempt.StartedAt = now;
            SubmissionExecutionRules.Transition(db, attempt, SubmissionAttemptStatuses.Submitting, adminUserId, now);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { throw SubmissionExecutionRules.VersionConflict(); }
            claimedVersion = attempt.Version;
            await transaction.CommitAsync(cancellationToken);
        }

        SubmissionResult result;
        try
        {
            result = await adapter.SubmitAsync(submissionRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = new(SubmissionOutcomes.Unknown, null, "ADAPTER_EXECUTION_CANCELLED",
                "Execution was interrupted after the submission claim; the external outcome requires reconciliation.");
        }
        catch
        {
            result = new(SubmissionOutcomes.Unknown, null, "ADAPTER_EXECUTION_EXCEPTION",
                "The adapter failed after execution may have started; the external outcome requires reconciliation.");
        }

        return await CompleteAsync(request.AttemptId, claimedVersion, result, adminUserId, CancellationToken.None);
    }

    private ISubmissionAdapter ResolveAdapter(string provider)
    {
        var matches = adapters.Where(candidate =>
            string.Equals(candidate.Provider.Trim(), provider, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            throw new ConflictException("SUBMISSION_ADAPTER_NOT_AVAILABLE",
                $"No submission adapter is configured for {provider}.");
        if (matches.Length > 1)
            throw new ServiceUnavailableException("SUBMISSION_ADAPTER_CONFIGURATION_INVALID",
                "Multiple submission adapters are configured for the same provider.");
        return matches[0];
    }

    private async Task<SubmissionAttemptResult> CompleteAsync(
        Guid attemptId,
        int claimedVersion,
        SubmissionResult providerResult,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await transactionFactory.BeginAsync(attemptId, cancellationToken);
        var attempt = transaction.Attempt ?? throw SubmissionExecutionRules.NotFound();
        SubmissionExecutionRules.RequireVersion(attempt, claimedVersion);
        SubmissionExecutionRules.RequireState(attempt, SubmissionAttemptStatuses.Submitting);
        var application = transaction.Application
            ?? throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");

        var outcome = SubmissionOutcomes.All.Contains(providerResult.Outcome)
            ? providerResult.Outcome
            : SubmissionOutcomes.Unknown;
        var target = outcome switch
        {
            SubmissionOutcomes.Success => SubmissionAttemptStatuses.Succeeded,
            SubmissionOutcomes.Failure => SubmissionAttemptStatuses.Failed,
            _ => SubmissionAttemptStatuses.Unknown,
        };
        var now = await SubmissionExecutionRules.NextEventTimeAsync(db, attempt.Id, clock.GetUtcNow(), cancellationToken);
        var providerSubmissionId = SubmissionExecutionRules.SafeText(providerResult.ProviderSubmissionId, 500);
        var failureCode = SubmissionExecutionRules.SafeCode(providerResult.FailureCode);
        var failureMessage = SubmissionExecutionRules.SafeText(providerResult.DiagnosticMessage, 2000);

        if (target == SubmissionAttemptStatuses.Succeeded)
        {
            try
            {
                SubmissionExecutionRules.RequireCurrentPackage(
                    application, transaction.ManagedCv, attempt, readinessEvaluator);
            }
            catch (ConflictException)
            {
                target = SubmissionAttemptStatuses.Unknown;
                failureCode = "LOCAL_STATE_CHANGED_AFTER_SUBMISSION";
                failureMessage = "The provider reported success, but local application state changed and requires reconciliation.";
            }
        }

        attempt.CompletedAt = now;
        attempt.ProviderSubmissionId = target is SubmissionAttemptStatuses.Succeeded or SubmissionAttemptStatuses.Unknown
            ? providerSubmissionId : null;
        attempt.FailureCode = target switch
        {
            SubmissionAttemptStatuses.Failed => failureCode ?? "PROVIDER_FAILURE",
            SubmissionAttemptStatuses.Unknown => failureCode ?? "PROVIDER_OUTCOME_UNKNOWN",
            _ => null,
        };
        attempt.FailureMessage = target is SubmissionAttemptStatuses.Failed or SubmissionAttemptStatuses.Unknown
            ? failureMessage : null;
        SubmissionExecutionRules.Transition(db, attempt, target, adminUserId, now);

        if (target == SubmissionAttemptStatuses.Succeeded)
        {
            if (providerSubmissionId is not null)
                application.ExternalApplicationId = providerSubmissionId;
            JobApplicationLifecycle.ApplyTransition(db, application, JobApplicationStatuses.Applied,
                adminUserId, $"Submission confirmed by {attempt.Provider}.", now, now);
        }

        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw SubmissionExecutionRules.VersionConflict(); }
        await transaction.CommitAsync(cancellationToken);
        return await SubmissionAttemptMapping.GetAsync(db, attempt.Id, cancellationToken);
    }
}

public sealed class ApproveSubmissionAttemptCommandValidator : IRequestValidator<ApproveSubmissionAttemptCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ApproveSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SubmissionExecutionRules.Validate(request.AttemptId, request.ExpectedVersion));
}

public sealed class ExecuteSubmissionAttemptCommandValidator : IRequestValidator<ExecuteSubmissionAttemptCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ExecuteSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SubmissionExecutionRules.Validate(request.AttemptId, request.ExpectedVersion));
}

internal static class SubmissionExecutionRules
{
    private static readonly TimeSpan PostgreSqlTimestampResolution = TimeSpan.FromTicks(10);

    public static List<ValidationFailure> Validate(Guid attemptId, int expectedVersion)
    {
        var failures = new List<ValidationFailure>();
        if (attemptId == Guid.Empty) failures.Add(new("attemptId", "Attempt ID is required."));
        if (expectedVersion < 1) failures.Add(new("expectedVersion", "Expected version must be positive."));
        return failures;
    }

    public static NotFoundException NotFound() =>
        new("SUBMISSION_ATTEMPT_NOT_FOUND", "The submission attempt was not found.");
    public static ConflictException VersionConflict() =>
        new("SUBMISSION_ATTEMPT_VERSION_CONFLICT", "The submission attempt has been modified.");
    public static void RequireVersion(SubmissionAttempt attempt, int expectedVersion)
    {
        if (attempt.Version != expectedVersion) throw VersionConflict();
    }
    public static void RequireState(SubmissionAttempt attempt, string required)
    {
        if (attempt.Status != required)
            throw new ConflictException("SUBMISSION_ATTEMPT_STATE_CONFLICT",
                $"The submission attempt must be {required}.");
    }

    public static FinalizedApplicationPackageHandoff RequireCurrentPackage(
        JobApplication application,
        JobApplicationDocument? managedCv,
        SubmissionAttempt attempt,
        ApplicationSubmissionReadinessEvaluator evaluator)
    {
        if (application.Version != attempt.ApplicationVersionAtCreation)
            throw new ConflictException("SUBMISSION_APPLICATION_CHANGED",
                "The application changed after this submission attempt was created.");
        var readiness = evaluator.Evaluate(application, managedCv);
        if (!readiness.IsReady)
        {
            var blocker = readiness.Blockers.First();
            throw new ConflictException(blocker.Code, blocker.Message);
        }
        var package = readiness.Package!;
        if (package.PackageRevision != attempt.PackageRevision)
            throw new ConflictException("SUBMISSION_PACKAGE_REVISION_CONFLICT",
                "The finalized package revision no longer matches this attempt.");
        if (!string.Equals(package.PackageManifestHash, attempt.PackageManifestHash, StringComparison.Ordinal))
            throw new ConflictException("SUBMISSION_PACKAGE_MANIFEST_CONFLICT",
                "The finalized package manifest no longer matches this attempt.");
        return package;
    }

    public static void Transition(
        IApplicationDbContext db,
        SubmissionAttempt attempt,
        string target,
        Guid actorAdminUserId,
        DateTimeOffset now)
    {
        if (!SubmissionAttemptStateMachine.CanTransition(attempt.Status, target))
            throw new ConflictException("SUBMISSION_ATTEMPT_TRANSITION_INVALID",
                $"Transition from {attempt.Status} to {target} is not allowed.");
        var from = attempt.Status;
        attempt.Status = target;
        attempt.Version++;
        db.SubmissionAttemptEvents.Add(new SubmissionAttemptEvent
        {
            Id = Guid.NewGuid(), SubmissionAttemptId = attempt.Id, FromStatus = from, ToStatus = target,
            ActorAdminUserId = actorAdminUserId, OccurredAt = now, CreatedAt = now,
        });
    }

    public static async Task<DateTimeOffset> NextEventTimeAsync(
        IApplicationDbContext db,
        Guid attemptId,
        DateTimeOffset candidate,
        CancellationToken cancellationToken)
    {
        var latest = await db.SubmissionAttemptEvents.AsNoTracking()
            .Where(item => item.SubmissionAttemptId == attemptId)
            .Select(item => (DateTimeOffset?)item.OccurredAt)
            .MaxAsync(cancellationToken);
        return latest is not null && candidate <= latest.Value
            ? latest.Value.Add(PostgreSqlTimestampResolution)
            : candidate;
    }

    public static string? SafeText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    public static string? SafeCode(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed)) return null;
        var safe = new string(trimmed.Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').ToArray());
        return string.IsNullOrEmpty(safe) ? null : safe[..Math.Min(safe.Length, 100)];
    }
}
