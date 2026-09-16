using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/integrations/telegram/webhook")]
public sealed class TelegramWebhookController(
    IRequestDispatcher dispatcher,
    IOptions<TelegramOptions> configured,
    IOptions<JsonOptions> jsonOptions) : ControllerBase
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (!options.Enabled)
        {
            return NotFound(ApiResponse.Failure(new ApiError(
                "INTEGRATION_UNAVAILABLE",
                "The integration endpoint is unavailable.")));
        }

        if (!Request.Headers.TryGetValue(SecretHeader, out var supplied)
            || !TelegramWebhookSecret.IsValid(supplied, options.WebhookSecret))
        {
            return Unauthorized(ApiResponse.Failure(new ApiError(
                "INVALID_WEBHOOK_SECRET",
                "Webhook authentication failed.")));
        }

        TelegramUpdateRequest? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<TelegramUpdateRequest>(
                Request.Body,
                jsonOptions.Value.JsonSerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse.Failure(new ApiError(
                "INVALID_TELEGRAM_UPDATE",
                "The Telegram update is invalid.")));
        }

        if (update is null)
        {
            return BadRequest(ApiResponse.Failure(new ApiError(
                "INVALID_TELEGRAM_UPDATE",
                "The Telegram update is invalid.")));
        }

        var message = update.Message;
        await dispatcher.DispatchAsync(new ProcessTelegramWebhookCommand(
            update.UpdateId,
            message?.MessageId,
            message?.Date,
            message?.Text,
            message?.Caption,
            message?.From?.Id,
            message?.Chat?.Id,
            message?.Chat?.Type), cancellationToken);
        return Ok(ApiResponse.Ok());
    }
}

public sealed record TelegramUpdateRequest(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessageRequest? Message);

public sealed record TelegramMessageRequest(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("date")] long? Date,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("caption")] string? Caption,
    [property: JsonPropertyName("from")] TelegramUserRequest? From,
    [property: JsonPropertyName("chat")] TelegramChatRequest? Chat);

public sealed record TelegramUserRequest([property: JsonPropertyName("id")] long Id);
public sealed record TelegramChatRequest(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type);

public static class TelegramWebhookSecret
{
    public static bool IsValid(StringValues supplied, string configured)
    {
        if (supplied.Count != 1 || string.IsNullOrEmpty(configured)) return false;
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied[0] ?? string.Empty);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return suppliedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }
}
