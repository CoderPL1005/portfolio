namespace Portfolio.Infrastructure.AI;

public sealed class GeminiSettings
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; init; } = "";
    public string ChatModel { get; init; } = "";
    public string EmbeddingModel { get; init; } = "";
    public int EmbeddingDimensions { get; init; } = 1536;
    public bool EnableIndexingWorker { get; init; }
}
