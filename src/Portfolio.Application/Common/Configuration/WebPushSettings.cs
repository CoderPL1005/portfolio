namespace Portfolio.Application.Common.Configuration;

public sealed class WebPushSettings
{
    public const string SectionName = "WebPush";

    public string Subject { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}
