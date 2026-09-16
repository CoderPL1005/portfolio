using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
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
    string? ChatType) : IRequest<TelegramWebhookResult>;

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
    public const string Unsupported = "⚠️ I couldn't store this message. Send or paste the job text or job URL.";

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
