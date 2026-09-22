using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record FinalizedApplicationCvResult(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string ContentHash,
    int PackageRevision,
    int SourceCanonicalCvVersion,
    DateTimeOffset CreatedAt);

public sealed record ApplicationPackageResult(
    string Status,
    int Revision,
    int? JobPostingVersion,
    string? ManifestHash,
    DateTimeOffset? FinalizedAt,
    Guid? FinalizedByAdminUserId,
    FinalizedApplicationCvResult? Cv);

public sealed record ApplicationPackageContentResult(byte[] Content, string FileName, string ContentType);
public sealed record GetApplicationPackageQuery(Guid ApplicationId) : IRequest<ApplicationPackageResult>;
public sealed record GetApplicationPackageContentQuery(Guid ApplicationId) : IRequest<ApplicationPackageContentResult>;
public sealed record FinalizeApplicationPackageCommand(
    Guid ApplicationId,
    int ExpectedApplicationVersion,
    int ExpectedJobPostingVersion,
    int ExpectedCanonicalCvVersion) : IRequest<ApplicationPackageResult>;

public static class ApplicationPackageManifest
{
    public static string Serialize(
        Guid applicationId,
        Guid jobPostingId,
        int jobPostingVersion,
        int packageRevision,
        Guid cvDocumentId,
        string cvFileName,
        string cvContentType,
        long cvFileSizeBytes,
        string cvContentHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("applicationId", applicationId);
            writer.WriteString("jobPostingId", jobPostingId);
            writer.WriteNumber("jobPostingVersion", jobPostingVersion);
            writer.WriteNumber("packageRevision", packageRevision);
            writer.WriteString("cvDocumentId", cvDocumentId);
            writer.WriteString("cvFileName", cvFileName);
            writer.WriteString("cvContentType", cvContentType);
            writer.WriteNumber("cvFileSizeBytes", cvFileSizeBytes);
            writer.WriteString("cvContentHash", cvContentHash);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Hash(string canonicalManifest) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalManifest))).ToLowerInvariant();
}

public sealed class GetApplicationPackageQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetApplicationPackageQuery, ApplicationPackageResult>
{
    public async Task<ApplicationPackageResult> HandleAsync(
        GetApplicationPackageQuery request,
        CancellationToken cancellationToken = default)
    {
        var application = await db.JobApplications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.ApplicationId, cancellationToken)
            ?? throw ApplicationPackageRules.ApplicationNotFound();
        var document = await db.JobApplicationDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.JobApplicationId == request.ApplicationId
                && item.DocumentType == "CV"
                && item.PackageRevision == 1
                && item.RemovedAt == null, cancellationToken);
        return ApplicationPackageRules.Map(application, document);
    }
}

public sealed class GetApplicationPackageContentQueryHandler(
    IApplicationDbContext db,
    IPrivateFileStorage storage,
    ILogger<GetApplicationPackageContentQueryHandler> logger)
    : IRequestHandler<GetApplicationPackageContentQuery, ApplicationPackageContentResult>
{
    public async Task<ApplicationPackageContentResult> HandleAsync(
        GetApplicationPackageContentQuery request,
        CancellationToken cancellationToken = default)
    {
        var application = await db.JobApplications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.ApplicationId, cancellationToken)
            ?? throw ApplicationPackageRules.ApplicationNotFound();
        if (application.PackageStatus != JobApplicationPackageStatuses.Finalized)
            throw new NotFoundException("APPLICATION_PACKAGE_NOT_FINALIZED", "The application package has not been finalized.");
        var document = await db.JobApplicationDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.JobApplicationId == request.ApplicationId
                && item.DocumentType == "CV"
                && item.PackageRevision == application.PackageRevision
                && item.RemovedAt == null, cancellationToken)
            ?? throw new ServiceUnavailableException("APPLICATION_PACKAGE_INVALID", "The finalized application package is incomplete.");
        var bytes = await ApplicationPackageRules.ReadAndVerifyAsync(
            storage, document.StorageKey!, document.FileSizeBytes!.Value, document.ContentHash!, logger,
            "finalized application CV", cancellationToken);
        return new(bytes, document.FileName!, document.ContentType!);
    }
}

public sealed class FinalizeApplicationPackageCommandHandler(
    IApplicationDbContext db,
    IPrivateFileStorage storage,
    IApplicationPackageFinalizationTransactionFactory transactionFactory,
    IApplicationPackageConflictDetector conflictDetector,
    ApplicationPackageReadinessEvaluator readinessEvaluator,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<FinalizeApplicationPackageCommandHandler> logger)
    : IRequestHandler<FinalizeApplicationPackageCommand, ApplicationPackageResult>
{
    public async Task<ApplicationPackageResult> HandleAsync(
        FinalizeApplicationPackageCommand request,
        CancellationToken cancellationToken = default)
    {
        var adminUserId = currentUser.AdminUserId
            ?? throw new UnauthorizedException("UNAUTHORIZED", "Authentication is required.");
        var initial = await ApplicationPackageRules.LoadInputsAsync(db, request.ApplicationId, cancellationToken)
            ?? throw ApplicationPackageRules.ApplicationNotFound();
        ApplicationPackageRules.RequireFinalizable(initial.Application, initial.JobPosting, initial.CanonicalCv, request, readinessEvaluator);

        var canonicalBytes = await ApplicationPackageRules.ReadAndVerifyAsync(
            storage, initial.CanonicalCv!.StorageKey, initial.CanonicalCv.FileSizeBytes,
            initial.CanonicalCv.ContentHash, logger, "canonical CV", cancellationToken);
        var documentId = Guid.NewGuid();
        var snapshotKey = $"applications/{request.ApplicationId:N}/{documentId:N}/cv.pdf";
        try
        {
            await using (var upload = new MemoryStream(canonicalBytes, writable: false))
                await storage.UploadAsync(snapshotKey, upload, CanonicalCvRules.PdfContentType, cancellationToken);

            await using var transaction = await transactionFactory.BeginAsync(request.ApplicationId, cancellationToken);
            var application = transaction.Application ?? throw ApplicationPackageRules.ApplicationNotFound();
            ApplicationPackageRules.RequireFinalizable(
                application, transaction.JobPosting, transaction.CanonicalCv, request, readinessEvaluator);

            var jobPosting = transaction.JobPosting!;
            var canonicalCv = transaction.CanonicalCv!;
            var now = clock.GetUtcNow();
            const int packageRevision = 1;
            var manifest = ApplicationPackageManifest.Serialize(
                application.Id, application.JobPostingId, jobPosting.Version, packageRevision,
                documentId, canonicalCv.FileName, canonicalCv.ContentType,
                canonicalCv.FileSizeBytes, canonicalCv.ContentHash);
            var manifestHash = ApplicationPackageManifest.Hash(manifest);
            var document = new JobApplicationDocument
            {
                Id = documentId,
                JobApplicationId = application.Id,
                DocumentType = "CV",
                VersionLabel = "Package revision 1",
                FileName = canonicalCv.FileName,
                StorageKey = snapshotKey,
                ContentHash = canonicalCv.ContentHash,
                ContentType = canonicalCv.ContentType,
                FileSizeBytes = canonicalCv.FileSizeBytes,
                PackageRevision = packageRevision,
                SourceCanonicalCvVersion = canonicalCv.Version,
                Metadata = JsonDocument.Parse("{}"),
                CreatedAt = now,
            };
            application.PackageStatus = JobApplicationPackageStatuses.Finalized;
            application.PackageRevision = packageRevision;
            application.PackageJobPostingVersion = jobPosting.Version;
            application.PackageManifestHash = manifestHash;
            application.PackageFinalizedAt = now;
            application.PackageFinalizedByAdminUserId = adminUserId;
            application.LastActivityAt = now;
            application.UpdatedAt = now;
            application.Version++;
            db.JobApplicationDocuments.Add(document);
            db.JobApplicationEvents.Add(new JobApplicationEvent
            {
                Id = Guid.NewGuid(),
                JobApplicationId = application.Id,
                EventType = JobApplicationEventTypes.PackageFinalized,
                ActorType = JobApplicationEventActorTypes.Admin,
                ActorAdminUserId = adminUserId,
                Note = "Application package finalized.",
                Metadata = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    packageRevision,
                    manifestHash,
                    cvDocumentId = documentId,
                })),
                OccurredAt = now,
                CreatedAt = now,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ApplicationPackageRules.FinalizationConflict();
            }
            catch (DbUpdateException exception) when (conflictDetector.IsManagedSnapshotConflict(exception))
            {
                throw ApplicationPackageRules.AlreadyFinalized();
            }
            await transaction.CommitAsync(cancellationToken);
            return ApplicationPackageRules.Map(application, document);
        }
        catch
        {
            await DeleteCompensationAsync(snapshotKey);
            throw;
        }
    }

    private async Task DeleteCompensationAsync(string storageKey)
    {
        try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Application package snapshot compensation failed for application {ApplicationId}.",
                storageKey.Split('/').ElementAtOrDefault(1));
        }
    }
}

public sealed class FinalizeApplicationPackageCommandValidator : IRequestValidator<FinalizeApplicationPackageCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        FinalizeApplicationPackageCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        AddVersion(failures, "expectedApplicationVersion", request.ExpectedApplicationVersion);
        AddVersion(failures, "expectedJobPostingVersion", request.ExpectedJobPostingVersion);
        AddVersion(failures, "expectedCanonicalCvVersion", request.ExpectedCanonicalCvVersion);
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }

    private static void AddVersion(List<ValidationFailure> failures, string property, int version)
    {
        if (version < 1) failures.Add(new(property, "Expected version must be greater than zero."));
    }
}

internal static class ApplicationPackageRules
{
    public static async Task<PackageInputs?> LoadInputsAsync(
        IApplicationDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        return await (
            from application in db.JobApplications.AsNoTracking()
            where application.Id == applicationId
            join jobPosting in db.JobPostings.AsNoTracking()
                on application.JobPostingId equals jobPosting.Id into jobPostings
            from jobPosting in jobPostings.DefaultIfEmpty()
            from canonicalCv in db.CanonicalCvs.AsNoTracking().DefaultIfEmpty()
            select new PackageInputs(application, jobPosting, canonicalCv))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static void RequireFinalizable(
        JobApplication application,
        JobPosting? jobPosting,
        CanonicalCv? canonicalCv,
        FinalizeApplicationPackageCommand request,
        ApplicationPackageReadinessEvaluator readinessEvaluator)
    {
        if (application.PackageStatus == JobApplicationPackageStatuses.Finalized)
            throw AlreadyFinalized();
        if (application.Version != request.ExpectedApplicationVersion)
            throw new ConflictException("JOB_APPLICATION_VERSION_CONFLICT", "The job application has been modified. Refresh and try again.");
        var readiness = readinessEvaluator.Evaluate(application, jobPosting, canonicalCv);
        if (!readiness.IsReady)
        {
            var blocker = readiness.Blockers.First();
            throw new ConflictException(blocker.Code, blocker.Message);
        }
        if (jobPosting!.Version != request.ExpectedJobPostingVersion)
            throw new ConflictException("JOB_POSTING_VERSION_CONFLICT", "The job posting has been modified. Refresh and try again.");
        if (canonicalCv!.Version != request.ExpectedCanonicalCvVersion)
            throw new ConflictException("CANONICAL_CV_VERSION_CONFLICT", "The canonical CV has been replaced. Refresh and try again.");
    }

    public static async Task<byte[]> ReadAndVerifyAsync(
        IPrivateFileStorage storage,
        string storageKey,
        long expectedSize,
        string expectedHash,
        ILogger logger,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await storage.OpenReadAsync(storageKey, CanonicalCvRules.MaxFileBytes, cancellationToken);
            using var target = new MemoryStream((int)Math.Min(expectedSize, CanonicalCvRules.MaxFileBytes));
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (target.Length + read > CanonicalCvRules.MaxFileBytes)
                    throw new InvalidDataException("The private PDF exceeds the permitted size.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var bytes = target.ToArray();
            var valid = bytes.LongLength == expectedSize
                && bytes.Length >= 5
                && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
                && CanonicalCvRules.Hash(bytes).Equals(expectedHash, StringComparison.Ordinal);
            if (!valid)
            {
                logger.LogError("The {Description} failed byte-level integrity verification.", description);
                throw new ServiceUnavailableException("APPLICATION_PACKAGE_CV_INVALID", "The canonical CV could not be verified safely.");
            }
            return bytes;
        }
        catch (ServiceUnavailableException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Private storage read failed while verifying the {Description}.", description);
            throw new ServiceUnavailableException("APPLICATION_PACKAGE_STORAGE_UNAVAILABLE", "The private CV is temporarily unavailable.");
        }
    }

    public static ApplicationPackageResult Map(JobApplication application, JobApplicationDocument? document)
    {
        if (application.PackageStatus == JobApplicationPackageStatuses.Finalized && document is null)
            throw new ServiceUnavailableException("APPLICATION_PACKAGE_INVALID", "The finalized application package is incomplete.");
        var cv = document is null ? null : new FinalizedApplicationCvResult(
            document.Id, document.FileName!, document.ContentType!, document.FileSizeBytes!.Value,
            document.ContentHash!, document.PackageRevision!.Value,
            document.SourceCanonicalCvVersion!.Value, document.CreatedAt);
        return new(application.PackageStatus, application.PackageRevision,
            application.PackageJobPostingVersion, application.PackageManifestHash,
            application.PackageFinalizedAt, application.PackageFinalizedByAdminUserId, cv);
    }

    public static NotFoundException ApplicationNotFound() =>
        new("JOB_APPLICATION_NOT_FOUND", "The job application was not found.");
    public static ConflictException AlreadyFinalized() =>
        new("APPLICATION_PACKAGE_ALREADY_FINALIZED", "The application package has already been finalized.");
    public static ConflictException FinalizationConflict() =>
        new("APPLICATION_PACKAGE_FINALIZATION_CONFLICT", "The application package changed while it was being finalized. Refresh and try again.");
}

internal sealed record PackageInputs(JobApplication Application, JobPosting? JobPosting, CanonicalCv? CanonicalCv);
