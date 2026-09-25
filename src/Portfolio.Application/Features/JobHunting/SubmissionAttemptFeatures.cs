using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record SubmissionAttemptEventResult(
    Guid Id, string? FromStatus, string ToStatus, Guid ActorAdminUserId,
    DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);

public sealed record SubmissionAttemptResult(
    Guid Id, Guid JobApplicationId, string Provider, string Status,
    int PackageRevision, string PackageManifestHash, int ApplicationVersionAtCreation,
    DateTimeOffset CreatedAt, Guid CreatedByAdminUserId, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt, string? ProviderSubmissionId, string? FailureCode,
    string? FailureMessage, int Version, IReadOnlyCollection<SubmissionAttemptEventResult> Events);

public sealed record CreateSubmissionAttemptCommand(
    Guid ApplicationId, string Provider, Guid ClientRequestId,
    int ExpectedApplicationVersion, int ExpectedPackageRevision, string ExpectedManifestHash)
    : IRequest<SubmissionAttemptResult>;
public sealed record GetSubmissionAttemptsQuery(Guid ApplicationId)
    : IRequest<IReadOnlyCollection<SubmissionAttemptResult>>;
public sealed record GetSubmissionAttemptQuery(Guid AttemptId)
    : IRequest<SubmissionAttemptResult>;

public static class SubmissionAttemptStateMachine
{
    public static bool CanTransition(string from, string to) => (from, to) switch
    {
        (SubmissionAttemptStatuses.Created, SubmissionAttemptStatuses.Approved) => true,
        (SubmissionAttemptStatuses.Approved, SubmissionAttemptStatuses.Submitting) => true,
        (SubmissionAttemptStatuses.Submitting, SubmissionAttemptStatuses.Succeeded) => true,
        (SubmissionAttemptStatuses.Submitting, SubmissionAttemptStatuses.Failed) => true,
        (SubmissionAttemptStatuses.Submitting, SubmissionAttemptStatuses.Unknown) => true,
        _ => false,
    };
}

public static class SubmissionAttemptIdempotency
{
    public static string Create(Guid applicationId, int packageRevision, string manifestHash, string provider, Guid clientRequestId)
    {
        var canonical = $"submission-attempt:v1|{applicationId:N}|{packageRevision}|{manifestHash}|{provider}|{clientRequestId:N}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed class CreateSubmissionAttemptCommandHandler(
    IApplicationDbContext db,
    ISubmissionAttemptCreationTransactionFactory transactionFactory,
    ISubmissionAttemptConflictDetector conflictDetector,
    ApplicationSubmissionReadinessEvaluator readinessEvaluator,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<CreateSubmissionAttemptCommand, SubmissionAttemptResult>
{
    public Task<SubmissionAttemptResult> HandleAsync(CreateSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        HandleCoreAsync(request, false, cancellationToken);

    internal Task<SubmissionAttemptResult> HandleAuthorizedEmailAsync(
        CreateSubmissionAttemptCommand request, CancellationToken cancellationToken = default) =>
        HandleCoreAsync(request, true, cancellationToken);

    private async Task<SubmissionAttemptResult> HandleCoreAsync(
        CreateSubmissionAttemptCommand request,
        bool authorizedEmailWorkflow,
        CancellationToken cancellationToken)
    {
        var adminUserId = currentUser.AdminUserId
            ?? throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        var provider = request.Provider.Trim().ToUpperInvariant();
        if (!SubmissionProviders.All.Contains(provider))
            throw new ValidationException([new("provider", "The submission provider is not supported.")]);
        if (provider == SubmissionProviders.Email && !authorizedEmailWorkflow)
            throw new ConflictException("EMAIL_SUBMISSION_REQUIRES_ORCHESTRATION",
                "Email submission attempts can only be created by the controlled email-application workflow.");

        await using var transaction = await transactionFactory.BeginAsync(request.ApplicationId, cancellationToken);
        var application = transaction.Application
            ?? throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");
        if (application.Version != request.ExpectedApplicationVersion)
            throw new ConflictException("JOB_APPLICATION_VERSION_CONFLICT", "The job application has been modified.");

        var readiness = readinessEvaluator.Evaluate(application, transaction.ManagedCv);
        if (!readiness.IsReady)
        {
            var blocker = readiness.Blockers.First();
            throw new ConflictException(blocker.Code, blocker.Message);
        }
        var package = readiness.Package!;
        if (package.PackageRevision != request.ExpectedPackageRevision)
            throw new ConflictException("SUBMISSION_PACKAGE_REVISION_CONFLICT", "The finalized package revision has changed.");
        if (!string.Equals(package.PackageManifestHash, request.ExpectedManifestHash, StringComparison.Ordinal))
            throw new ConflictException("SUBMISSION_PACKAGE_MANIFEST_CONFLICT", "The finalized package manifest has changed.");

        var idempotencyKey = SubmissionAttemptIdempotency.Create(
            application.Id, package.PackageRevision, package.PackageManifestHash, provider, request.ClientRequestId);
        var existingId = await db.SubmissionAttempts.AsNoTracking()
            .Where(attempt => attempt.IdempotencyKey == idempotencyKey)
            .Select(attempt => (Guid?)attempt.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId is not null)
            return await SubmissionAttemptMapping.GetAsync(db, existingId.Value, cancellationToken);
        var nonRetryableAttemptExists = await db.SubmissionAttempts.AsNoTracking().AnyAsync(
            attempt => attempt.JobApplicationId == application.Id &&
                attempt.PackageRevision == package.PackageRevision &&
                attempt.Provider == provider &&
                attempt.Status != SubmissionAttemptStatuses.Failed,
            cancellationToken);
        if (nonRetryableAttemptExists)
            throw new ConflictException("SUBMISSION_ATTEMPT_ACTIVE_EXISTS",
                "A non-retryable submission attempt already exists for this package and provider.");

        var now = clock.GetUtcNow();
        var attempt = new SubmissionAttempt
        {
            Id = Guid.NewGuid(), JobApplicationId = application.Id, Provider = provider,
            Status = SubmissionAttemptStatuses.Created, IdempotencyKey = idempotencyKey,
            PackageRevision = package.PackageRevision, PackageManifestHash = package.PackageManifestHash,
            ApplicationVersionAtCreation = application.Version, CreatedAt = now,
            CreatedByAdminUserId = adminUserId, Version = 1,
        };
        var history = new SubmissionAttemptEvent
        {
            Id = Guid.NewGuid(), SubmissionAttemptId = attempt.Id, ToStatus = SubmissionAttemptStatuses.Created,
            ActorAdminUserId = adminUserId, OccurredAt = now, CreatedAt = now,
        };
        db.SubmissionAttempts.Add(attempt);
        db.SubmissionAttemptEvents.Add(history);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (conflictDetector.IsDuplicateIdempotencyKey(exception))
        {
            throw new ConflictException("SUBMISSION_ATTEMPT_ALREADY_EXISTS", "The logical submission attempt already exists.");
        }
        catch (DbUpdateException exception) when (conflictDetector.IsNonRetryableAttemptConflict(exception))
        {
            throw new ConflictException("SUBMISSION_ATTEMPT_ACTIVE_EXISTS",
                "A non-retryable submission attempt already exists for this package and provider.");
        }
        await transaction.CommitAsync(cancellationToken);
        return await SubmissionAttemptMapping.GetAsync(db, attempt.Id, cancellationToken);
    }
}

public sealed class GetSubmissionAttemptsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSubmissionAttemptsQuery, IReadOnlyCollection<SubmissionAttemptResult>>
{
    public async Task<IReadOnlyCollection<SubmissionAttemptResult>> HandleAsync(GetSubmissionAttemptsQuery request, CancellationToken cancellationToken = default)
    {
        if (!await db.JobApplications.AsNoTracking().AnyAsync(application => application.Id == request.ApplicationId, cancellationToken))
            throw new NotFoundException("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");
        var query = db.SubmissionAttempts.AsNoTracking()
            .Where(attempt => attempt.JobApplicationId == request.ApplicationId)
            .OrderByDescending(attempt => attempt.CreatedAt).ThenByDescending(attempt => attempt.Id)
            .AsQueryable();
        return await SubmissionAttemptMapping.Project(query).ToListAsync(cancellationToken);
    }
}

public sealed class GetSubmissionAttemptQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSubmissionAttemptQuery, SubmissionAttemptResult>
{
    public Task<SubmissionAttemptResult> HandleAsync(GetSubmissionAttemptQuery request, CancellationToken cancellationToken = default) =>
        SubmissionAttemptMapping.GetAsync(db, request.AttemptId, cancellationToken);
}

public sealed class CreateSubmissionAttemptCommandValidator : IRequestValidator<CreateSubmissionAttemptCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateSubmissionAttemptCommand request, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        if (request.ApplicationId == Guid.Empty) failures.Add(new("applicationId", "Application ID is required."));
        if (string.IsNullOrWhiteSpace(request.Provider)) failures.Add(new("provider", "Provider is required."));
        else if (request.Provider.Trim().Length > 30) failures.Add(new("provider", "Provider cannot exceed 30 characters."));
        if (request.ClientRequestId == Guid.Empty) failures.Add(new("clientRequestId", "Client request ID is required."));
        if (request.ExpectedApplicationVersion < 1) failures.Add(new("expectedApplicationVersion", "Expected application version must be positive."));
        if (request.ExpectedPackageRevision != 1) failures.Add(new("expectedPackageRevision", "Expected package revision must be 1."));
        if (!IsLowercaseSha256(request.ExpectedManifestHash)) failures.Add(new("expectedManifestHash", "Expected manifest hash must be a lowercase SHA-256 value."));
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }

    private static bool IsLowercaseSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class SubmissionAttemptMapping
{
    public static IQueryable<SubmissionAttemptResult> Project(IQueryable<SubmissionAttempt> query) => query
        .Select(attempt => new SubmissionAttemptResult(
            attempt.Id, attempt.JobApplicationId, attempt.Provider, attempt.Status,
            attempt.PackageRevision, attempt.PackageManifestHash, attempt.ApplicationVersionAtCreation,
            attempt.CreatedAt, attempt.CreatedByAdminUserId, attempt.StartedAt, attempt.CompletedAt,
            attempt.ProviderSubmissionId, attempt.FailureCode, attempt.FailureMessage, attempt.Version,
            attempt.Events.OrderBy(history => history.OccurredAt).ThenBy(history => history.Id)
                .Select(history => new SubmissionAttemptEventResult(
                    history.Id, history.FromStatus, history.ToStatus, history.ActorAdminUserId,
                    history.OccurredAt, history.CreatedAt)).ToList()));

    public static async Task<SubmissionAttemptResult> GetAsync(IApplicationDbContext db, Guid attemptId, CancellationToken cancellationToken) =>
        await Project(db.SubmissionAttempts.AsNoTracking().Where(attempt => attempt.Id == attemptId))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("SUBMISSION_ATTEMPT_NOT_FOUND", "The submission attempt was not found.");
}
