using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Integrations;

public interface ITelegramBotClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
}

public interface IIngestionKeyConflictDetector
{
    bool IsIngestionKeyConflict(DbUpdateException exception);
}
