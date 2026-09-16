using System.Net.Http.Json;
using System.Text.Json.Serialization;
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


    public async Task<TelegramDownloadedFile> DownloadFileAsync(
        string fileId,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || maximumBytes <= 0)
        {
            throw new TelegramDeliveryException("Telegram file could not be downloaded.");
        }

        var token = configured.Value.BotToken;
        using var metadataResponse = await httpClient.GetAsync(
            $"https://api.telegram.org/bot{token}/getFile?file_id={Uri.EscapeDataString(fileId)}",
            cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode)
        {
            throw new TelegramDeliveryException("Telegram file could not be downloaded.");
        }

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<TelegramFileResponse>(cancellationToken);
        var filePath = metadata is { Ok: true } ? metadata.Result?.FilePath : null;
        if (!TelegramFilePath.IsSafe(filePath))
        {
            throw new TelegramDeliveryException("Telegram file could not be downloaded.");
        }

        var escapedPath = string.Join('/', filePath!.Split('/').Select(Uri.EscapeDataString));
        using var downloadResponse = await httpClient.GetAsync(
            $"https://api.telegram.org/file/bot{token}/{escapedPath}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var contentLength = downloadResponse.Content.Headers.ContentLength;
        if (!downloadResponse.IsSuccessStatusCode
            || contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new TelegramDeliveryException("Telegram file could not be downloaded.");
        }

        await using var source = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (target.Length + read > maximumBytes)
            {
                throw new TelegramDeliveryException("Telegram file could not be downloaded.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return new(target.ToArray(), downloadResponse.Content.Headers.ContentType?.MediaType);
    }
}

public sealed class TelegramDeliveryException(string message) : Exception(message);

internal sealed record TelegramFileResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] TelegramFileResult? Result);

internal sealed record TelegramFileResult(
    [property: JsonPropertyName("file_path")] string? FilePath);

internal static class TelegramFilePath
{
    public static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.StartsWith('/') || value.Contains('\\')) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out _)) return false;
        var segments = value.Split('/');
        return segments.Length > 0
            && segments.All(segment => segment.Length > 0
                && segment is not "." and not ".."
                && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'));
    }
}

public sealed class NpgsqlIngestionKeyConflictDetector : IIngestionKeyConflictDetector
{
    public bool IsIngestionKeyConflict(Microsoft.EntityFrameworkCore.DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_raw_job_postings_ingestion_key",
        };


    public bool IsAttachmentDeliveryConflict(Microsoft.EntityFrameworkCore.DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_raw_job_posting_attachments_delivery",
        };
}
