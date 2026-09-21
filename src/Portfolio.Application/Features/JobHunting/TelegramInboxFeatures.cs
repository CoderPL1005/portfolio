using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Configuration;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record ProcessTelegramWebhookCommand(
    long UpdateId,
    long? MessageId,
    long? MessageDateUnix,
    string? Text,
    string? Caption,
    long? SenderId,
    long? ChatId,
    string? ChatType,
    string? MediaGroupId = null,
    IReadOnlyCollection<TelegramPhotoSize>? Photo = null) : IRequest<TelegramWebhookResult>;

public sealed record TelegramPhotoSize(
    string FileId,
    string FileUniqueId,
    int Width,
    int Height,
    long? FileSize);

public sealed record TelegramWebhookResult(string Status, Guid? RawJobPostingId = null, string? Source = null);

public static class TelegramIngestionStatuses
{
    public const string Created = "CREATED";
    public const string AlreadyReceived = "ALREADY_RECEIVED";
    public const string Unsupported = "UNSUPPORTED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Disabled = "DISABLED";
}

public static class TelegramAcknowledgements
{
    public const string Unsupported = "⚠️ I couldn't store this message. Send a screenshot/photo, job text, or job URL.";
    public const string ScreenshotReceived = "✅ Job screenshot received\nStatus: Pending analysis";

    public static string Received(string source) => $"✅ Job received\nSource: {DisplaySource(source)}\nStatus: Pending analysis";

    public static string DisplaySource(string source) => source switch
    {
        RawJobPostingSources.Facebook => "Facebook",
        RawJobPostingSources.Instagram => "Instagram",
        RawJobPostingSources.TopCv => "TopCV",
        RawJobPostingSources.VietnamWorks => "VietnamWorks",
        RawJobPostingSources.Manual => "Manual",
        _ => "Other",
    };
}

public sealed class ProcessTelegramWebhookCommandHandler(
    IApplicationDbContext db,
    ITelegramBotClient telegram,
    IFileStorage storage,
    IIngestionKeyConflictDetector conflictDetector,
    IOptions<TelegramOptions> configured,
    TimeProvider clock,
    ILogger<ProcessTelegramWebhookCommandHandler> logger)
    : IRequestHandler<ProcessTelegramWebhookCommand, TelegramWebhookResult>
{
    public async Task<TelegramWebhookResult> HandleAsync(
        ProcessTelegramWebhookCommand request,
        CancellationToken cancellationToken = default)
    {
        var options = configured.Value;
        if (!options.Enabled) return new(TelegramIngestionStatuses.Disabled);

        if (request.SenderId != options.AllowedUserId
            || request.ChatId != options.AllowedChatId
            || !string.Equals(request.ChatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Rejected unauthorized Telegram inbox update {UpdateId}.", request.UpdateId);
            return new(TelegramIngestionStatuses.Unauthorized);
        }

        if (request.Photo is { Count: > 0 })
        {
            return await HandlePhotoAsync(request, options, cancellationToken);
        }

        return await HandleTextAsync(request, options, cancellationToken);
    }

    private async Task<TelegramWebhookResult> HandleTextAsync(
        ProcessTelegramWebhookCommand request,
        TelegramOptions options,
        CancellationToken cancellationToken)
    {
        var rawContent = TelegramInboxMapping.ExtractContent(request.Text, request.Caption);
        if (request.MessageId is null or <= 0 || rawContent is null || rawContent.Length > 200_000)
        {
            await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.Unsupported, request.UpdateId, cancellationToken);
            return new(TelegramIngestionStatuses.Unsupported);
        }

        var ingestionKey = TelegramInboxMapping.BuildIngestionKey(options.AllowedChatId, request.MessageId.Value);
        var existing = await db.RawJobPostings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IngestionKey == ingestionKey, cancellationToken);
        if (existing is not null)
        {
            return new(TelegramIngestionStatuses.AlreadyReceived, existing.Id, existing.Source);
        }

        var sourceUrl = TelegramInboxMapping.SelectSourceUrl(rawContent);
        var source = TelegramInboxMapping.ClassifySource(sourceUrl);
        var now = clock.GetUtcNow();
        var discoveredAt = TelegramInboxMapping.ResolveMessageTime(request.MessageDateUnix, now);
        var entity = new RawJobPosting
        {
            Id = Guid.NewGuid(),
            JobPostingId = null,
            Source = source,
            SourceExternalId = null,
            IngestionKey = ingestionKey,
            SourceUrl = sourceUrl,
            SourceUrlHash = sourceUrl is null ? null : JobHashing.Sha256(JobHashing.NormalizeUrl(sourceUrl)),
            RawContent = rawContent,
            ContentHash = JobHashing.Sha256(JobHashing.NormalizeText(rawContent)),
            CompanyTitleFingerprint = null,
            IngestionStatus = RawJobPostingIngestionStatuses.Received,
            DuplicateOfRawJobPostingId = null,
            Metadata = JsonSerializer.SerializeToDocument(new
            {
                ingestionChannel = "TELEGRAM",
                telegramUpdateId = request.UpdateId,
                telegramMessageId = request.MessageId.Value,
                telegramMessageDateUtc = discoveredAt,
            }),
            DiscoveredAt = discoveredAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.RawJobPostings.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (conflictDetector.IsIngestionKeyConflict(exception))
        {
            db.RawJobPostings.Entry(entity).State = EntityState.Detached;
            existing = await db.RawJobPostings.AsNoTracking()
                .SingleAsync(item => item.IngestionKey == ingestionKey, cancellationToken);
            return new(TelegramIngestionStatuses.AlreadyReceived, existing.Id, existing.Source);
        }

        logger.LogInformation(
            "Stored Telegram inbox update {UpdateId} as raw job {RawJobPostingId} with source {Source}.",
            request.UpdateId,
            entity.Id,
            source);
        await TryAcknowledgeAsync(
            options.AllowedChatId,
            TelegramAcknowledgements.Received(source),
            request.UpdateId,
            cancellationToken);
        return new(TelegramIngestionStatuses.Created, entity.Id, source);
    }

    private async Task<TelegramWebhookResult> HandlePhotoAsync(
        ProcessTelegramWebhookCommand request,
        TelegramOptions options,
        CancellationToken cancellationToken)
    {
        var photo = TelegramInboxMapping.SelectPhoto(request.Photo!);
        var caption = TelegramInboxMapping.ExtractContent(null, request.Caption);
        var albumId = TelegramInboxMapping.NormalizeMediaGroupId(request.MediaGroupId);
        if (request.MessageId is null or <= 0
            || photo is null
            || caption?.Length > 200_000
            || request.MediaGroupId is not null && albumId is null
            || photo.FileSize > options.MaxImageBytes)
        {
            await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.Unsupported, request.UpdateId, cancellationToken);
            return new(TelegramIngestionStatuses.Unsupported);
        }

        var isAlbum = albumId is not null;
        var ingestionKey = isAlbum
            ? TelegramInboxMapping.BuildAlbumIngestionKey(options.AllowedChatId, albumId!)
            : TelegramInboxMapping.BuildIngestionKey(options.AllowedChatId, request.MessageId.Value);

        var existing = await db.RawJobPostings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IngestionKey == ingestionKey, cancellationToken);
        if (existing is not null)
        {
            if (await db.RawJobPostingAttachments.AsNoTracking().AnyAsync(
                item => item.RawJobPostingId == existing.Id && item.TelegramMessageId == request.MessageId.Value,
                cancellationToken))
            {
                if (isAlbum)
                {
                    await RefreshAlbumAggregateAsync(existing.Id, request, albumId!, cancellationToken);
                }
                return new(TelegramIngestionStatuses.AlreadyReceived, existing.Id, existing.Source);
            }

            if (await db.RawJobPostingAttachments.CountAsync(
                item => item.RawJobPostingId == existing.Id,
                cancellationToken) >= options.MaxAlbumImages)
            {
                await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.Unsupported, request.UpdateId, cancellationToken);
                return new(TelegramIngestionStatuses.Unsupported, existing.Id, existing.Source);
            }
        }

        var downloaded = await telegram.DownloadFileAsync(photo.FileId, options.MaxImageBytes, cancellationToken);
        if (!TelegramImageRules.TryInspect(downloaded, options.MaxImageBytes, out var image))
        {
            await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.Unsupported, request.UpdateId, cancellationToken);
            return new(TelegramIngestionStatuses.Unsupported);
        }

        DbUpdateException? lastConflict = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var raw = await db.RawJobPostings
                .SingleOrDefaultAsync(item => item.IngestionKey == ingestionKey, cancellationToken);
            var currentAttachments = raw is null
                ? []
                : await db.RawJobPostingAttachments.AsNoTracking()
                    .Where(item => item.RawJobPostingId == raw.Id)
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .ToListAsync(cancellationToken);

            var duplicate = currentAttachments.SingleOrDefault(item => item.TelegramMessageId == request.MessageId.Value);
            if (duplicate is not null)
            {
                return new(TelegramIngestionStatuses.AlreadyReceived, raw!.Id, raw.Source);
            }
            if (currentAttachments.Count >= options.MaxAlbumImages)
            {
                await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.Unsupported, request.UpdateId, cancellationToken);
                return new(TelegramIngestionStatuses.Unsupported, raw!.Id, raw.Source);
            }

            var now = clock.GetUtcNow();
            var rawId = raw?.Id ?? Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var storageKey = $"job-hunting/raw/{rawId:N}/{attachmentId:N}{image.Extension}";
            await using var imageStream = new MemoryStream(downloaded.Content, writable: false);
            try
            {
                await storage.UploadPrivateAsync(storageKey, imageStream, image.ContentType, cancellationToken);
            }
            catch
            {
                await TryDeleteStorageAsync(storageKey, cancellationToken);
                throw;
            }

            var attachment = new RawJobPostingAttachment
            {
                Id = attachmentId,
                RawJobPostingId = rawId,
                AttachmentType = RawJobPostingAttachmentTypes.Image,
                StorageKey = storageKey,
                ContentType = image.ContentType,
                ContentHash = image.ContentHash,
                FileSizeBytes = downloaded.Content.LongLength,
                SortOrder = request.MessageId.Value,
                TelegramMessageId = request.MessageId.Value,
                TelegramFileId = photo.FileId,
                TelegramFileUniqueId = photo.FileUniqueId,
                Width = photo.Width,
                Height = photo.Height,
                CreatedAt = now,
            };

            var orderedHashes = currentAttachments
                .Append(attachment)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => item.ContentHash)
                .ToArray();
            var rawContent = caption ?? (isAlbum ? "[telegram-image-album]" : "[telegram-image]");
            var sourceUrl = caption is null ? null : TelegramInboxMapping.SelectSourceUrl(caption);
            var source = TelegramInboxMapping.ClassifySource(sourceUrl);
            if (raw is null)
            {
                raw = new RawJobPosting
                {
                    Id = rawId,
                    JobPostingId = null,
                    Source = source,
                    SourceExternalId = null,
                    IngestionKey = ingestionKey,
                    SourceUrl = sourceUrl,
                    SourceUrlHash = sourceUrl is null ? null : JobHashing.Sha256(JobHashing.NormalizeUrl(sourceUrl)),
                    RawContent = rawContent,
                    ContentHash = isAlbum ? TelegramImageRules.AlbumHash(orderedHashes) : image.ContentHash,
                    CompanyTitleFingerprint = null,
                    IngestionStatus = RawJobPostingIngestionStatuses.Received,
                    DuplicateOfRawJobPostingId = null,
                    Metadata = TelegramImageRules.Metadata(request, albumId, 1, now),
                    DiscoveredAt = TelegramInboxMapping.ResolveMessageTime(request.MessageDateUnix, now),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.RawJobPostings.Add(raw);
            }
            else
            {
                if ((raw.RawContent is "[telegram-image]" or "[telegram-image-album]") && caption is not null)
                {
                    raw.RawContent = caption;
                    raw.Source = source;
                    raw.SourceUrl = sourceUrl;
                    raw.SourceUrlHash = sourceUrl is null ? null : JobHashing.Sha256(JobHashing.NormalizeUrl(sourceUrl));
                }
                raw.ContentHash = TelegramImageRules.AlbumHash(orderedHashes);
                raw.Metadata = TelegramImageRules.Metadata(request, albumId, currentAttachments.Count + 1, now, raw.Metadata);
                raw.UpdatedAt = now;
                raw.Version++;
            }
            db.RawJobPostingAttachments.Add(attachment);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                exception is DbUpdateConcurrencyException
                || conflictDetector.IsIngestionKeyConflict(exception)
                || conflictDetector.IsAttachmentDeliveryConflict(exception))
            {
                lastConflict = exception;
                db.RawJobPostingAttachments.Entry(attachment).State = EntityState.Detached;
                db.RawJobPostings.Entry(raw).State = EntityState.Detached;
                await TryDeleteStorageAsync(storageKey, cancellationToken);
                continue;
            }
            catch
            {
                await TryDeleteStorageAsync(storageKey, cancellationToken);
                throw;
            }

            if (isAlbum)
            {
                await RefreshAlbumAggregateAsync(raw.Id, request, albumId!, cancellationToken);
            }

            logger.LogInformation(
                "Stored Telegram image update {UpdateId} as attachment {AttachmentId} for raw job {RawJobPostingId}.",
                request.UpdateId,
                attachment.Id,
                raw.Id);
            if (!isAlbum)
            {
                await TryAcknowledgeAsync(options.AllowedChatId, TelegramAcknowledgements.ScreenshotReceived, request.UpdateId, cancellationToken);
            }
            return new(TelegramIngestionStatuses.Created, raw.Id, raw.Source);
        }

        var winner = await db.RawJobPostings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IngestionKey == ingestionKey, cancellationToken);
        if (winner is not null && await db.RawJobPostingAttachments.AsNoTracking().AnyAsync(
            item => item.RawJobPostingId == winner.Id && item.TelegramMessageId == request.MessageId.Value,
            cancellationToken))
        {
            if (isAlbum)
            {
                await RefreshAlbumAggregateAsync(winner.Id, request, albumId!, cancellationToken);
            }
            return new(TelegramIngestionStatuses.AlreadyReceived, winner.Id, winner.Source);
        }
        throw lastConflict ?? new DbUpdateException("Telegram image delivery could not be persisted.");
    }

    private async Task RefreshAlbumAggregateAsync(
        Guid rawJobPostingId,
        ProcessTelegramWebhookCommand request,
        string mediaGroupId,
        CancellationToken cancellationToken)
    {
        DbUpdateConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var raw = await db.RawJobPostings.SingleAsync(item => item.Id == rawJobPostingId, cancellationToken);
            var hashes = await db.RawJobPostingAttachments.AsNoTracking()
                .Where(item => item.RawJobPostingId == rawJobPostingId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => item.ContentHash)
                .ToListAsync(cancellationToken);
            var now = clock.GetUtcNow();
            raw.ContentHash = TelegramImageRules.AlbumHash(hashes);
            raw.Metadata = TelegramImageRules.Metadata(request, mediaGroupId, hashes.Count, now, raw.Metadata);
            raw.UpdatedAt = now;
            raw.Version++;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                lastConflict = exception;
                db.RawJobPostings.Entry(raw).State = EntityState.Detached;
            }
        }
        throw lastConflict!;
    }

    private async Task TryDeleteStorageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try { await storage.DeleteAsync(storageKey, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { logger.LogWarning("Telegram image storage compensation failed for key {StorageKey}.", storageKey); }
    }

    private async Task TryAcknowledgeAsync(long chatId, string message, long updateId, CancellationToken cancellationToken)
    {
        try
        {
            await telegram.SendMessageAsync(chatId, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Telegram acknowledgement failed for update {UpdateId}.", updateId);
        }
    }
}

public static partial class TelegramInboxMapping
{
    private static readonly string[] TrailingUrlPunctuation = [".", ",", ";", ":", "!", "?", ")", "]", "}", "'", "\""];

    [GeneratedRegex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();

    public static string? ExtractContent(string? text, string? caption) =>
        !string.IsNullOrWhiteSpace(text) ? text : !string.IsNullOrWhiteSpace(caption) ? caption : null;

    public static string BuildIngestionKey(long chatId, long messageId) =>
        FormattableString.Invariant($"telegram:{chatId}:{messageId}");

    public static string BuildAlbumIngestionKey(long chatId, string mediaGroupId) =>
        FormattableString.Invariant($"telegram-album:{chatId}:{mediaGroupId}");

    public static string? NormalizeMediaGroupId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= 100
            && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? normalized
            : null;
    }

    public static TelegramPhotoSize? SelectPhoto(IReadOnlyCollection<TelegramPhotoSize> photos) =>
        photos.Where(item => !string.IsNullOrWhiteSpace(item.FileId)
                && !string.IsNullOrWhiteSpace(item.FileUniqueId)
                && item.FileId.Length <= 512
                && item.FileUniqueId.Length <= 512
                && item.Width > 0
                && item.Height > 0
                && item.FileSize is null or > 0)
            .OrderByDescending(item => (long)item.Width * item.Height)
            .ThenByDescending(item => item.FileSize ?? 0)
            .ThenBy(item => item.FileId, StringComparer.Ordinal)
            .FirstOrDefault();

    public static DateTimeOffset ResolveMessageTime(long? unixSeconds, DateTimeOffset fallback)
    {
        if (unixSeconds is null) return fallback;
        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value); }
        catch (ArgumentOutOfRangeException) { return fallback; }
    }

    public static string? SelectSourceUrl(string content)
    {
        var valid = new List<(string Value, bool Recognized)>();
        foreach (Match match in HttpUrlRegex().Matches(content))
        {
            var candidate = TrimTrailingPunctuation(match.Value);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(uri.Host)) continue;
            valid.Add((candidate, ClassifySource(candidate) != RawJobPostingSources.Manual));
        }

        return valid.FirstOrDefault(item => item.Recognized).Value
            ?? valid.FirstOrDefault().Value;
    }

    public static string ClassifySource(string? sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)) return RawJobPostingSources.Manual;
        var host = uri.IdnHost;
        if (IsHost(host, "facebook.com") || IsHost(host, "fb.com")) return RawJobPostingSources.Facebook;
        if (IsHost(host, "instagram.com")) return RawJobPostingSources.Instagram;
        if (IsHost(host, "topcv.vn")) return RawJobPostingSources.TopCv;
        if (IsHost(host, "vietnamworks.com")) return RawJobPostingSources.VietnamWorks;
        return RawJobPostingSources.Manual;
    }

    private static bool IsHost(string host, string expected) =>
        host.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{expected}", StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingPunctuation(string value)
    {
        while (TrailingUrlPunctuation.Any(item => value.EndsWith(item, StringComparison.Ordinal))) value = value[..^1];
        return value;
    }
}

internal sealed record TelegramImageInfo(string ContentType, string Extension, string ContentHash);

internal static class TelegramImageRules
{
    public static bool TryInspect(
        TelegramDownloadedFile downloaded,
        long maximumBytes,
        out TelegramImageInfo image)
    {
        image = null!;
        var bytes = downloaded.Content;
        if (bytes.Length == 0 || bytes.LongLength > maximumBytes) return false;

        string contentType;
        string extension;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
        {
            contentType = "image/jpeg";
            extension = ".jpg";
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            contentType = "image/png";
            extension = ".png";
        }
        else
        {
            return false;
        }

        var declared = downloaded.ContentType?.Split(';', 2)[0].Trim();
        if (!string.IsNullOrEmpty(declared)
            && !declared.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !declared.Equals(contentType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        image = new(contentType, extension, JobHashing.Sha256Bytes(bytes));
        return true;
    }

    public static string AlbumHash(IEnumerable<string> orderedHashes) =>
        JobHashing.Sha256("telegram-image-album:v1\n" + string.Join('\n', orderedHashes));

    public static JsonDocument Metadata(
        ProcessTelegramWebhookCommand request,
        string? mediaGroupId,
        int attachmentCount,
        DateTimeOffset fallback,
        JsonDocument? existing = null)
    {
        var metadata = existing?.RootElement.ValueKind == JsonValueKind.Object
            ? existing.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        metadata["ingestionChannel"] = JsonSerializer.SerializeToElement("TELEGRAM");
        metadata["telegramUpdateId"] = JsonSerializer.SerializeToElement(request.UpdateId);
        metadata["telegramMessageId"] = JsonSerializer.SerializeToElement(request.MessageId);
        metadata["telegramMessageDateUtc"] = JsonSerializer.SerializeToElement(TelegramInboxMapping.ResolveMessageTime(request.MessageDateUnix, fallback));
        metadata["telegramMediaGroupId"] = JsonSerializer.SerializeToElement(mediaGroupId);
        metadata["attachmentCount"] = JsonSerializer.SerializeToElement(attachmentCount);
        return JsonSerializer.SerializeToDocument(metadata);
    }
}
