namespace Portfolio.Application.Common.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public long AllowedUserId { get; set; }
    public long AllowedChatId { get; set; }
}
