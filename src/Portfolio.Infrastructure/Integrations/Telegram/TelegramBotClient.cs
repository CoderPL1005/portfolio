using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Configuration;

namespace Portfolio.Infrastructure.Integrations.Telegram;

public sealed class TelegramBotClient(HttpClient httpClient, IOptions<TelegramOptions> configured)
    : ITelegramBotClient
{
    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        var token = configured.Value.BotToken;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.telegram.org/bot{token}/sendMessage")
        {
            Content = JsonContent.Create(new { chat_id = chatId, text }),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramDeliveryException("Telegram acknowledgement could not be delivered.");
        }
    }
}

public sealed class TelegramDeliveryException(string message) : Exception(message);

public sealed class NpgsqlIngestionKeyConflictDetector : IIngestionKeyConflictDetector
{
    public bool IsIngestionKeyConflict(Microsoft.EntityFrameworkCore.DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_raw_job_postings_ingestion_key",
        };
}
