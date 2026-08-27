namespace Portfolio.Domain.Entities;

public sealed class AgentSetting
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool Enabled { get; set; }
    public string? Provider { get; set; }
    public string? ModelName { get; set; }
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }
    public int EmbeddingDimensions { get; set; }
    public string SystemPrompt { get; set; } = null!;
    public string? WelcomeMessage { get; set; }
    public string? FallbackMessage { get; set; }
    public int MaxContextChunks { get; set; }
    public decimal? MinimumSimilarity { get; set; }
    public decimal Temperature { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
