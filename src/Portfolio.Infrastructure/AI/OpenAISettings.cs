namespace Portfolio.Infrastructure.AI;
public sealed class OpenAISettings{public const string SectionName="OpenAI";public string ApiKey{get;init;}="";public string ChatModel{get;init;}="";public string EmbeddingModel{get;init;}="";public int EmbeddingDimensions{get;init;}=1536;public bool EnableIndexingWorker{get;init;}}
