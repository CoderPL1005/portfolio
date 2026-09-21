using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record AnalyzeRawJobPostingCommand(Guid Id) : IRequest<JobPostingResult>;
public sealed record GetRawJobPostingsQuery(int Page, int PageSize, string? IngestionStatus)
    : IRequest<PagedResult<RawJobPostingListItem>>;
public sealed record RawJobPostingListItem(
    Guid Id,
    string Source,
    string IngestionStatus,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset CreatedAt,
    int AttachmentCount,
    int Version);

public sealed class AnalyzeRawJobPostingCommandValidator : IRequestValidator<AnalyzeRawJobPostingCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(AnalyzeRawJobPostingCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(
            request.Id == Guid.Empty ? [new("id", "Raw job posting ID is required.")] : []);
}

public sealed class GetRawJobPostingsQueryValidator : IRequestValidator<GetRawJobPostingsQuery>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetRawJobPostingsQuery request, CancellationToken cancellationToken = default)
    {
        var failures = JobValidation.Paging(request.Page, request.PageSize);
        JobValidation.OptionalClosed(failures, "ingestionStatus", request.IngestionStatus,
            [RawJobPostingIngestionStatuses.Received, RawJobPostingIngestionStatuses.Normalized,
             RawJobPostingIngestionStatuses.Duplicate, RawJobPostingIngestionStatuses.Rejected]);
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}

public sealed class GetRawJobPostingsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetRawJobPostingsQuery, PagedResult<RawJobPostingListItem>>
{
    public async Task<PagedResult<RawJobPostingListItem>> HandleAsync(GetRawJobPostingsQuery request, CancellationToken cancellationToken = default)
    {
        var query = db.RawJobPostings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.IngestionStatus))
        {
            var status = request.IngestionStatus.Trim().ToUpperInvariant();
            query = query.Where(item => item.IngestionStatus == status);
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new RawJobPostingListItem(
                item.Id, item.Source, item.IngestionStatus, item.DiscoveredAt, item.CreatedAt,
                item.Attachments.Count, item.Version))
            .ToListAsync(cancellationToken);
        return new(items, request.Page, request.PageSize, total);
    }
}

public sealed class AnalyzeRawJobPostingCommandHandler(
    IApplicationDbContext db,
    IFileStorage storage,
    IJobAnalysisService analysis,
    TimeProvider clock,
    ILogger<AnalyzeRawJobPostingCommandHandler> logger)
    : IRequestHandler<AnalyzeRawJobPostingCommand, JobPostingResult>
{
    private const long MaximumImageBytes = 10 * 1024 * 1024;
    private const long MaximumTotalBytes = 50 * 1024 * 1024;
    private const int MaximumImages = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobPostingResult> HandleAsync(AnalyzeRawJobPostingCommand request, CancellationToken cancellationToken = default)
    {
        var raw = await db.RawJobPostings.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("RAW_JOB_POSTING_NOT_FOUND", "The raw job posting was not found.");
        if (raw.JobPostingId.HasValue && raw.IngestionStatus == RawJobPostingIngestionStatuses.Normalized)
        {
            return await JobPostingMapping.GetAsync(db, raw.JobPostingId.Value, cancellationToken);
        }
        if (raw.JobPostingId.HasValue || raw.IngestionStatus != RawJobPostingIngestionStatuses.Received)
        {
            throw new ConflictException("RAW_JOB_POSTING_NOT_ANALYZABLE", "The raw job posting is not eligible for analysis.");
        }

        var attachments = await db.RawJobPostingAttachments.AsNoTracking()
            .Where(item => item.RawJobPostingId == raw.Id)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        ValidateAttachments(attachments);

        var fingerprint = Fingerprint(analysis.ModelIdentifier, attachments);
        var extracted = ReadCachedResult(raw.Metadata, fingerprint);
        var cacheHit = extracted is not null;
        if (extracted is null)
        {
            var images = await LoadImagesAsync(attachments, cancellationToken);
            try
            {
                extracted = await analysis.AnalyzeAsync(new JobAnalysisRequest(images), cancellationToken);
            }
            catch (JobAnalysisRequestTooLargeException)
            {
                throw Invalid("The screenshots are too large to analyze together. Submit a smaller screenshot set.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Job extraction provider failed for raw job posting {RawJobPostingId}.", raw.Id);
                throw new ServiceUnavailableException("JOB_EXTRACTION_UNAVAILABLE", "Job screenshot analysis is temporarily unavailable.");
            }
        }

        extracted = NormalizeAndValidate(extracted);
        var now = clock.GetUtcNow();
        if (!cacheHit)
        {
            raw.Metadata = WriteCache(raw.Metadata, fingerprint, analysis.ModelIdentifier, extracted, now);
            raw.Version++;
            raw.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.RawJobPostings.Entry(raw).State = EntityState.Detached;
                raw = await db.RawJobPostings.SingleAsync(item => item.Id == request.Id, cancellationToken);
                if (raw.JobPostingId.HasValue && raw.IngestionStatus == RawJobPostingIngestionStatuses.Normalized)
                {
                    return await JobPostingMapping.GetAsync(db, raw.JobPostingId.Value, cancellationToken);
                }
                extracted = ReadCachedResult(raw.Metadata, fingerprint);
                if (extracted is null)
                {
                    throw new ConflictException("RAW_JOB_POSTING_VERSION_CONFLICT", "The raw job posting was modified. Retry analysis.");
                }
                extracted = NormalizeAndValidate(extracted);
            }
        }

        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            VerificationStatus = JobPostingVerificationStatuses.Pending,
            SelectionStatus = JobPostingSelectionStatuses.PendingAnalysis,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var stack = JsonDocument.Parse(JsonSerializer.Serialize(extracted.TechnologyStack, JsonOptions));
        JobPostingMapping.Assign(
            posting, extracted.CompanyName!, extracted.PositionTitle!, extracted.Location!, extracted.EmploymentType,
            extracted.WorkplaceType, extracted.SalaryMinimum, extracted.SalaryMaximum, extracted.SalaryCurrency,
            extracted.SalaryPeriod, extracted.ExperienceRequirements, extracted.Description!, stack.RootElement,
            extracted.ApplicationEmail, extracted.ApplicationUrl, extracted.ExpiresAt, null);
        raw.JobPostingId = posting.Id;
        raw.IngestionStatus = RawJobPostingIngestionStatuses.Normalized;
        raw.CompanyTitleFingerprint = JobHashing.Fingerprint(posting.CompanyName, posting.PositionTitle);
        raw.Version++;
        raw.UpdatedAt = now;
        db.JobPostings.Add(posting);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.JobPostings.Entry(posting).State = EntityState.Detached;
            db.RawJobPostings.Entry(raw).State = EntityState.Detached;
            var winner = await db.RawJobPostings.AsNoTracking().SingleAsync(item => item.Id == request.Id, cancellationToken);
            if (winner.JobPostingId.HasValue && winner.IngestionStatus == RawJobPostingIngestionStatuses.Normalized)
            {
                return await JobPostingMapping.GetAsync(db, winner.JobPostingId.Value, cancellationToken);
            }
            throw new ConflictException("RAW_JOB_POSTING_VERSION_CONFLICT", "The raw job posting was modified. Retry analysis.");
        }

        logger.LogInformation(
            "Raw job posting {RawJobPostingId} normalized from {AttachmentCount} attachments using model {Model}, schema {SchemaVersion}, prompt {PromptVersion}, cache hit {CacheHit}.",
            raw.Id, attachments.Count, analysis.ModelIdentifier, JobAnalysisContract.SchemaVersion,
            JobAnalysisContract.PromptVersion, cacheHit);
        return await JobPostingMapping.GetAsync(db, posting.Id, cancellationToken);
    }

    private async Task<IReadOnlyList<JobAnalysisImage>> LoadImagesAsync(
        IReadOnlyList<RawJobPostingAttachment> attachments,
        CancellationToken cancellationToken)
    {
        var images = new List<JobAnalysisImage>(attachments.Count);
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            byte[] bytes;
            try
            {
                await using var stream = await storage.OpenReadAsync(attachment.StorageKey, MaximumImageBytes, cancellationToken);
                using var target = new MemoryStream((int)attachment.FileSizeBytes);
                await stream.CopyToAsync(target, cancellationToken);
                bytes = target.ToArray();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Private screenshot read failed for raw job posting {RawJobPostingId}.", attachment.RawJobPostingId);
                throw new ServiceUnavailableException("JOB_SCREENSHOT_STORAGE_UNAVAILABLE", "A stored screenshot could not be loaded for analysis.");
            }
            var hash = JobHashing.Sha256Bytes(bytes);
            if (bytes.LongLength != attachment.FileSizeBytes
                || !hash.Equals(attachment.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("A stored screenshot failed its integrity check.");
            }
            images.Add(new(index + 1, attachment.ContentType, bytes, attachment.ContentHash));
        }
        return images;
    }

    private static void ValidateAttachments(IReadOnlyCollection<RawJobPostingAttachment> attachments)
    {
        if (attachments.Count is < 1 or > MaximumImages) throw Invalid("Between 1 and 10 screenshots are required for analysis.");
        if (attachments.Any(item => item.AttachmentType != RawJobPostingAttachmentTypes.Image)) throw Invalid("Only image attachments can be analyzed.");
        if (attachments.Any(item => item.ContentType is not ("image/jpeg" or "image/png"))) throw Invalid("Only JPEG and PNG screenshots can be analyzed.");
        if (attachments.Any(item => item.FileSizeBytes is <= 0 or > MaximumImageBytes)) throw Invalid("A screenshot exceeds the analysis size limit.");
        if (attachments.Sum(item => item.FileSizeBytes) > MaximumTotalBytes) throw Invalid("The screenshot submission exceeds the analysis size limit.");
    }

    private static JobAnalysisResult NormalizeAndValidate(JobAnalysisResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Assessment != JobAnalysisAssessments.SingleJobPosting)
        {
            throw Invalid(value.Assessment switch
            {
                JobAnalysisAssessments.NotAJobPosting => "The screenshots do not appear to contain a job posting.",
                JobAnalysisAssessments.MultipleJobPostings => "The screenshots appear to contain multiple different jobs.",
                _ => "The screenshots are unreadable or do not contain enough job information.",
            });
        }
        if (value.ConflictingFields is null || value.TechnologyStack is null)
            throw Invalid("The screenshot analysis result is incomplete.");
        if (value.ConflictingFields.Count > 0) throw Invalid("The screenshots contain conflicting job information.");

        var company = RequiredText(value.CompanyName, "companyName");
        var title = RequiredText(value.PositionTitle, "positionTitle");
        var location = RequiredText(value.Location, "location");
        var description = RequiredLongText(value.Description, "description");
        var technologies = value.TechnologyStack.Select(NormalizeShortText).Where(item => item is not null)
            .Select(item => item!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var stack = JsonDocument.Parse(JsonSerializer.Serialize(technologies, JsonOptions));
        var normalized = new JobAnalysisResult(
            value.Assessment, company, title, location, OptionalText(value.EmploymentType), OptionalText(value.WorkplaceType),
            value.SalaryMinimum, value.SalaryMaximum, OptionalText(value.SalaryCurrency)?.ToUpperInvariant(),
            OptionalText(value.SalaryPeriod), OptionalLongText(value.ExperienceRequirements), description, technologies,
            OptionalText(value.ApplicationEmail), OptionalText(value.ApplicationUrl), value.ExpiresAt, []);
        var failures = JobValidation.Posting(
            null, null, null, null, company, title, location, normalized.EmploymentType, normalized.WorkplaceType,
            normalized.SalaryMinimum, normalized.SalaryMaximum, normalized.SalaryCurrency, normalized.SalaryPeriod,
            description, stack.RootElement, normalized.ApplicationEmail, normalized.ApplicationUrl, null);
        if (failures.Count > 0) throw new ValidationException(failures);
        return normalized;
    }

    private static string RequiredText(string? value, string field)
    {
        var result = NormalizeShortText(value);
        if (result is null || Placeholder(result)) throw new ValidationException([new(field, $"{field} is required and must be visible in the screenshots.")]);
        return result;
    }

    private static string RequiredLongText(string? value, string field)
    {
        var result = OptionalLongText(value);
        if (result is null || Placeholder(result)) throw new ValidationException([new(field, $"{field} is required and must be visible in the screenshots.")]);
        return result;
    }

    private static string? OptionalText(string? value)
    {
        var result = NormalizeShortText(value);
        return result is null || Placeholder(result) ? null : result;
    }

    private static string? NormalizeShortText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : Regex.Replace(value.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");
    private static string? OptionalLongText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : JobHashing.NormalizeText(value);
    private static bool Placeholder(string value) => value.Trim().ToLowerInvariant() is "unknown" or "n/a" or "not specified" or "not available" or "unspecified";

    private static string Fingerprint(string model, IReadOnlyList<RawJobPostingAttachment> attachments) => JobHashing.Sha256(
        "job-extraction\n" + JobAnalysisContract.SchemaVersion + "\n" + JobAnalysisContract.PromptVersion + "\n"
        + model.Trim().ToLowerInvariant() + "\n"
        + string.Join('\n', attachments.Select(item => $"{item.SortOrder}:{item.Id:N}:{item.ContentHash.ToLowerInvariant()}")));

    private static JobAnalysisResult? ReadCachedResult(JsonDocument metadata, string fingerprint)
    {
        try
        {
            if (!metadata.RootElement.TryGetProperty("jobExtraction", out var cache)
                || !cache.TryGetProperty("fingerprint", out var cachedFingerprint)
                || !fingerprint.Equals(cachedFingerprint.GetString(), StringComparison.Ordinal)
                || !cache.TryGetProperty("validatedResult", out var result)) return null;
            return result.Deserialize<JobAnalysisResult>(JsonOptions);
        }
        catch (JsonException) { return null; }
    }

    private static JsonDocument WriteCache(
        JsonDocument metadata,
        string fingerprint,
        string model,
        JobAnalysisResult result,
        DateTimeOffset analyzedAt)
    {
        var values = metadata.RootElement.ValueKind == JsonValueKind.Object
            ? metadata.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        values["jobExtraction"] = JsonSerializer.SerializeToElement(new
        {
            fingerprint,
            model,
            schemaVersion = JobAnalysisContract.SchemaVersion,
            promptVersion = JobAnalysisContract.PromptVersion,
            validatedResult = result,
            analyzedAt,
        }, JsonOptions);
        return JsonDocument.Parse(JsonSerializer.Serialize(values, JsonOptions));
    }

    private static ValidationException Invalid(string message) => new([new("screenshots", message)]);
}
