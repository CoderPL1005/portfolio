using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record ScreenshotUpload(Stream Content, string ContentType, long FileSize);
public sealed record SubmitJobScreenshotsCommand(Guid SubmissionId, IReadOnlyCollection<ScreenshotUpload> Files)
    : IRequest<ScreenshotSubmissionResult>;
public sealed record ScreenshotSubmissionResult(
    Guid RawJobPostingId,
    string IngestionStatus,
    int AttachmentCount,
    bool Created);

public sealed class SubmitJobScreenshotsCommandValidator : IRequestValidator<SubmitJobScreenshotsCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        SubmitJobScreenshotsCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        if (request.SubmissionId == Guid.Empty)
        {
            failures.Add(new("submissionId", "Submission ID is required."));
        }
        if (request.Files.Count is < 1 or > ScreenshotImageRules.MaxFiles)
        {
            failures.Add(new("files", $"Between 1 and {ScreenshotImageRules.MaxFiles} screenshots are required."));
        }
        if (request.Files.Any(file => file.FileSize is <= 0 or > ScreenshotImageRules.MaxFileBytes))
        {
            failures.Add(new("files", $"Each screenshot must be between 1 and {ScreenshotImageRules.MaxFileBytes} bytes."));
        }
        if (request.Files.Sum(file => file.FileSize) > ScreenshotImageRules.MaxTotalBytes)
        {
            failures.Add(new("files", $"The total screenshot payload cannot exceed {ScreenshotImageRules.MaxTotalBytes} bytes."));
        }
        if (request.Files.Any(file => !ScreenshotImageRules.AllowedContentTypes.Contains(file.ContentType)))
        {
            failures.Add(new("files", "Only JPEG and PNG screenshots are allowed."));
        }
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}

public sealed class SubmitJobScreenshotsCommandHandler(
    IApplicationDbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    IIngestionKeyConflictDetector conflictDetector,
    TimeProvider clock,
    ILogger<SubmitJobScreenshotsCommandHandler> logger)
    : IRequestHandler<SubmitJobScreenshotsCommand, ScreenshotSubmissionResult>
{
    public async Task<ScreenshotSubmissionResult> HandleAsync(
        SubmitJobScreenshotsCommand request,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.AdminUserId is null)
        {
            throw new UnauthorizedAccessException("An authenticated administrator is required.");
        }

        var ingestionKey = $"pwa:{currentUser.AdminUserId.Value:N}:{request.SubmissionId:N}";
        var existing = await FindExistingAsync(ingestionKey, cancellationToken);
        if (existing is not null) return existing;

        var images = new List<ValidatedScreenshot>(request.Files.Count);
        foreach (var file in request.Files)
        {
            images.Add(await ScreenshotImageRules.ReadAndValidateAsync(file, cancellationToken));
        }

        var now = clock.GetUtcNow();
        var rawId = Guid.NewGuid();
        var uploadedKeys = new List<string>(images.Count);
        var attachments = new List<RawJobPostingAttachment>(images.Count);
        try
        {
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                var attachmentId = Guid.NewGuid();
                var storageKey = $"job-hunting/raw/{rawId:N}/{attachmentId:N}{image.Extension}";
                await using var content = new MemoryStream(image.Content, writable: false);
                uploadedKeys.Add(storageKey);
                await storage.UploadPrivateAsync(storageKey, content, image.ContentType, cancellationToken);
                attachments.Add(new RawJobPostingAttachment
                {
                    Id = attachmentId,
                    RawJobPostingId = rawId,
                    AttachmentType = RawJobPostingAttachmentTypes.Image,
                    StorageKey = storageKey,
                    ContentType = image.ContentType,
                    ContentHash = image.ContentHash,
                    FileSizeBytes = image.Content.LongLength,
                    SortOrder = index + 1,
                    TelegramMessageId = null,
                    TelegramFileId = null,
                    TelegramFileUniqueId = null,
                    Width = image.Width,
                    Height = image.Height,
                    CreatedAt = now,
                });
            }

            var raw = new RawJobPosting
            {
                Id = rawId,
                JobPostingId = null,
                Source = RawJobPostingSources.Manual,
                SourceExternalId = null,
                IngestionKey = ingestionKey,
                SourceUrl = null,
                SourceUrlHash = null,
                RawContent = "[pwa-image-submission]",
                ContentHash = JobHashing.Sha256("pwa-image-submission:v1\n" + string.Join('\n', attachments.Select(item => item.ContentHash))),
                CompanyTitleFingerprint = null,
                IngestionStatus = RawJobPostingIngestionStatuses.Received,
                DuplicateOfRawJobPostingId = null,
                Metadata = JsonSerializer.SerializeToDocument(new
                {
                    ingestionChannel = "PWA",
                    submissionId = request.SubmissionId,
                    attachmentCount = attachments.Count,
                }),
                DiscoveredAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.RawJobPostings.Add(raw);
            db.RawJobPostingAttachments.AddRange(attachments);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (conflictDetector.IsIngestionKeyConflict(exception))
            {
                Detach(raw, attachments);
                await DeleteUploadedAsync(uploadedKeys);
                return await FindExistingAsync(ingestionKey, cancellationToken)
                    ?? throw new DbUpdateException("The winning screenshot submission could not be loaded.", exception);
            }

            return new(raw.Id, raw.IngestionStatus, attachments.Count, true);
        }
        catch
        {
            await DeleteUploadedAsync(uploadedKeys);
            throw;
        }
    }

    private async Task<ScreenshotSubmissionResult?> FindExistingAsync(string ingestionKey, CancellationToken cancellationToken)
    {
        var row = await db.RawJobPostings.AsNoTracking()
            .Where(item => item.IngestionKey == ingestionKey)
            .Select(item => new
            {
                item.Id,
                item.IngestionStatus,
                AttachmentCount = item.Attachments.Count,
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.Id, row.IngestionStatus, row.AttachmentCount, false);
    }

    private void Detach(RawJobPosting raw, IEnumerable<RawJobPostingAttachment> attachments)
    {
        foreach (var attachment in attachments) db.RawJobPostingAttachments.Entry(attachment).State = EntityState.Detached;
        db.RawJobPostings.Entry(raw).State = EntityState.Detached;
    }

    private async Task DeleteUploadedAsync(IEnumerable<string> storageKeys)
    {
        foreach (var storageKey in storageKeys.Reverse())
        {
            try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
            catch (Exception exception) { logger.LogWarning(exception, "Screenshot storage compensation failed for key {StorageKey}.", storageKey); }
        }
    }
}

internal sealed record ValidatedScreenshot(
    byte[] Content,
    string ContentType,
    string Extension,
    string ContentHash,
    int Width,
    int Height);

internal static class ScreenshotImageRules
{
    public const int MaxFiles = 10;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxTotalBytes = 50 * 1024 * 1024;
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    public static async Task<ValidatedScreenshot> ReadAndValidateAsync(
        ScreenshotUpload upload,
        CancellationToken cancellationToken)
    {
        await using var target = new MemoryStream((int)Math.Min(upload.FileSize, MaxFileBytes));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await upload.Content.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (target.Length + read > MaxFileBytes)
            {
                throw Invalid("A screenshot exceeds the permitted file size.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var bytes = target.ToArray();
        if (bytes.Length == 0 || bytes.LongLength != upload.FileSize)
        {
            throw Invalid("A screenshot is empty or its reported size is invalid.");
        }

        string detectedType;
        string extension;
        int width;
        int height;
        if (TryReadPng(bytes, out width, out height))
        {
            detectedType = "image/png";
            extension = ".png";
        }
        else if (TryReadJpeg(bytes, out width, out height))
        {
            detectedType = "image/jpeg";
            extension = ".jpg";
        }
        else
        {
            throw Invalid("A screenshot does not contain a valid JPEG or PNG image header.");
        }
        if (!detectedType.Equals(upload.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("A screenshot's declared content type does not match its content.");
        }
        if (width <= 0 || height <= 0)
        {
            throw Invalid("A screenshot has invalid dimensions.");
        }
        return new(bytes, detectedType, extension, JobHashing.Sha256Bytes(bytes), width, height);
    }

    private static bool TryReadPng(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (bytes.Length < 24 || !bytes.StartsWith(signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length) return false;
            if (IsStartOfFrame(marker))
            {
                if (length < 7) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }
            offset += length;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static ValidationException Invalid(string message) => new([new("files", message)]);
}
