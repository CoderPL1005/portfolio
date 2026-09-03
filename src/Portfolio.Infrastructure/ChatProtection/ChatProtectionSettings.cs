namespace Portfolio.Infrastructure.ChatProtection;

public sealed class ChatProtectionSettings
{
    public const string SectionName = "ChatProtection";

    public int BurstPermitLimit { get; init; } = 3;
    public int BurstWindowSeconds { get; init; } = 60;
    public int DailyPerVisitorLimit { get; init; } = 20;
    public int SessionUserMessageLimit { get; init; } = 20;
    public int GlobalDailyLimit { get; init; } = 150;
    public string IpHashSecret { get; init; } = "";
}
