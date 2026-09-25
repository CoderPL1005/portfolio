using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Configuration;
using Portfolio.Domain.Constants;

namespace Portfolio.Infrastructure.Integrations.Gmail;

public sealed class GmailSubmissionAdapter(
    IApplicationDbContext db,
    IPrivateFileStorage storage,
    IApplicationEmailSender sender,
    IApplicationEmailComposer composer,
    IOptions<EmailSubmissionOptions> configured,
    ILogger<GmailSubmissionAdapter> logger) : ISubmissionAdapter
{
    private const long MaximumCvBytes = 10 * 1024 * 1024;
    public string Provider => SubmissionProviders.Email;
    public SubmissionAdapterCapabilities Capabilities => new(true, false, true, false, true);

    public bool Supports(SubmissionRequest request) => request.Provider == Provider
        && EmailSubmissionOptionsValidator.ValidEmail(request.ApplicationEmail)
        && request.CvContentType == "application/pdf"
        && request.CvFileSizeBytes is >= 1 and <= MaximumCvBytes;

    public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        sender.EnsureCanSend(request.ApplicationEmail!);
        var inputs = await (
            from application in db.JobApplications.AsNoTracking()
            where application.Id == request.ApplicationId && application.JobPostingId == request.JobPostingId
            join job in db.JobPostings.AsNoTracking() on application.JobPostingId equals job.Id
            join document in db.JobApplicationDocuments.AsNoTracking() on application.Id equals document.JobApplicationId
            where document.Id == request.CvDocumentId && document.RemovedAt == null
            select new { Application = application, Job = job, Document = document })
            .SingleOrDefaultAsync(cancellationToken);
        if (inputs is null || !string.Equals(inputs.Application.ApplicationEmail, request.ApplicationEmail, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(inputs.Job.ApplicationEmail, request.ApplicationEmail, StringComparison.OrdinalIgnoreCase)
            || inputs.Application.PackageJobPostingVersion != inputs.Job.Version
            || inputs.Document.PackageRevision != request.PackageRevision
            || !string.Equals(inputs.Document.ContentHash, request.CvContentHash, StringComparison.Ordinal))
            return new(SubmissionOutcomes.Failure, null, "EMAIL_AUTHORITATIVE_DATA_CHANGED",
                "The authoritative application email or finalized CV changed before sending.");

        var profile = await db.Profiles.AsNoTracking().Where(item => item.SingletonKey == 1)
            .Select(item => item.FullName).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(profile))
            return new(SubmissionOutcomes.Failure, null, "CANDIDATE_PROFILE_MISSING", "Candidate profile information is unavailable.");

        byte[] bytes;
        try
        {
            await using var source = await storage.OpenReadAsync(inputs.Document.StorageKey!, MaximumCvBytes, cancellationToken);
            using var target = new MemoryStream((int)request.CvFileSizeBytes);
            await source.CopyToAsync(target, cancellationToken);
            bytes = target.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (bytes.LongLength != request.CvFileSizeBytes || bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
                || !hash.Equals(request.CvContentHash, StringComparison.Ordinal))
                return new(SubmissionOutcomes.Failure, null, "APPLICATION_PACKAGE_CV_INVALID", "The finalized CV failed integrity verification.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Finalized CV read failed for submission attempt {SubmissionAttemptId} with {ErrorType}.",
                request.SubmissionAttemptId, exception.GetType().Name);
            return new(SubmissionOutcomes.Failure, null, "APPLICATION_PACKAGE_STORAGE_UNAVAILABLE", "The finalized CV is temporarily unavailable.");
        }

        var message = composer.Compose(request.ApplicationEmail!, configured.Value.SenderEmail, profile,
            inputs.Job.CompanyName, inputs.Job.PositionTitle, request.CvFileName, request.CvContentType, bytes);
        return await sender.SendAsync(message, cancellationToken);
    }
}
