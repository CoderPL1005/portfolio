namespace Portfolio.Application.Common.Abstractions.AI;

public interface IEmbeddingService { Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default); }
public interface IRetrievalQueryRewriter { Task<RetrievalQueryRewriteResult> RewriteAsync(string originalMessage, CancellationToken cancellationToken = default); }
public interface IChatCompletionService { Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default); }
public interface IKnowledgeRetriever { Task<IReadOnlyCollection<RetrievedKnowledge>> RetrieveAsync(float[] embedding, int topK, decimal? minimumSimilarity, CancellationToken cancellationToken = default); }
public interface IKnowledgeIndexer { Task IndexPendingAsync(CancellationToken cancellationToken = default); }
public sealed record ChatHistoryItem(string Role,string Content);
public sealed record RetrievalQueryRewriteResult(bool Usable,string? Query)
{
    public static RetrievalQueryRewriteResult Unusable { get; } = new(false, null);
}
public sealed record ChatCompletionRequest(string SystemInstructions,string UserMessage,IReadOnlyCollection<ChatHistoryItem> History,IReadOnlyCollection<RetrievedKnowledge> Context,decimal Temperature,int MaxOutputTokens);
public sealed record ChatCompletionResult(string Content,string? ModelName,int? PromptTokens,int? CompletionTokens,int? LatencyMs);
public sealed record RetrievedKnowledge(Guid ChunkId,Guid DocumentId,string Title,string SourceType,Guid? SourceRefId,string? ProjectSlug,string Content,int Rank,decimal SimilarityScore);
