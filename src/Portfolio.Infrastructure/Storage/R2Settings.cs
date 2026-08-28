namespace Portfolio.Infrastructure.Storage;

public sealed class R2Settings
{
    public const string SectionName = "R2";
    public string AccountId { get; init; } = "";
    public string AccessKeyId { get; init; } = "";
    public string SecretAccessKey { get; init; } = "";
    public string BucketName { get; init; } = "";
    public string PublicBaseUrl { get; init; } = "";
}
