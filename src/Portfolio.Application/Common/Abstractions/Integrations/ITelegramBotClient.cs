using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Integrations;

public interface ITelegramBotClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    Task<TelegramDownloadedFile> DownloadFileAsync(string fileId, long maximumBytes, CancellationToken cancellationToken = default);
}

public sealed record TelegramDownloadedFile(byte[] Content, string? ContentType);

public interface IIngestionKeyConflictDetector
{
    bool IsIngestionKeyConflict(DbUpdateException exception);
    bool IsAttachmentDeliveryConflict(DbUpdateException exception);
}
