using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Configuration;

namespace Portfolio.Infrastructure.Integrations.Telegram;

public sealed partial class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    [GeneratedRegex("^[A-Za-z0-9_-]{32,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookSecretPattern();

    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BotToken)) failures.Add("Telegram:BotToken is required when Telegram is enabled.");
        if (!WebhookSecretPattern().IsMatch(options.WebhookSecret ?? string.Empty))
        {
            failures.Add("Telegram:WebhookSecret must contain 32 to 256 Telegram-compatible characters.");
        }
        if (options.AllowedUserId == 0) failures.Add("Telegram:AllowedUserId must be a non-zero Int64 value.");
        if (options.AllowedChatId == 0) failures.Add("Telegram:AllowedChatId must be a non-zero Int64 value.");
        if (options.MaxImageBytes is < 1 or > 20 * 1024 * 1024) failures.Add("Telegram:MaxImageBytes must be between 1 and 20971520 bytes.");
        if (options.MaxAlbumImages is < 1 or > 20) failures.Add("Telegram:MaxAlbumImages must be between 1 and 20.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
