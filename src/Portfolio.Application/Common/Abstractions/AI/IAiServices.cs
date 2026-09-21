namespace Portfolio.Application.Common.Abstractions.AI;

public interface IEmbeddingService { Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default); }
public interface IRetrievalQueryRewriter { Task<RetrievalQueryRewriteResult> RewriteAsync(string originalMessage, CancellationToken cancellationToken = default); }
public interface IChatCompletionService { Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default); }
public interface IJobAnalysisService
{
    string ModelIdentifier { get; }
    Task<JobAnalysisResult> AnalyzeAsync(JobAnalysisRequest request, CancellationToken cancellationToken = default);
}
public interface IKnowledgeRetriever
{
    Task<IReadOnlyCollection<RetrievedKnowledge>> RetrieveAsync(float[] embedding, int topK, decimal? minimumSimilarity, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RetrievedKnowledge>> RetrieveCollectionAsync(float[] embedding, IReadOnlyList<KnowledgeCollectionMember> canonicalMembers, CancellationToken cancellationToken = default);
}
public interface IKnowledgeIndexer { Task IndexPendingAsync(CancellationToken cancellationToken = default); }
public sealed record ChatHistoryItem(string Role,string Content);
public sealed record RetrievalQueryRewriteResult(bool Usable,string? Query)
{
    public static RetrievalQueryRewriteResult Unusable { get; } = new(false, null);
}
public sealed record ChatCompletionRequest(string SystemInstructions,string UserMessage,IReadOnlyCollection<ChatHistoryItem> History,IReadOnlyCollection<RetrievedKnowledge> Context,decimal Temperature,int MaxOutputTokens);
public sealed record ChatCompletionResult(string Content,string? ModelName,int? PromptTokens,int? CompletionTokens,int? LatencyMs);
public sealed record JobAnalysisImage(int Order, string ContentType, byte[] Content, string ContentHash);
public sealed record JobAnalysisRequest(IReadOnlyList<JobAnalysisImage> Images);
public sealed record JobAnalysisResult(
    string Assessment,
    string? CompanyName,
    string? PositionTitle,
    string? Location,
    string? EmploymentType,
    string? WorkplaceType,
    decimal? SalaryMinimum,
    decimal? SalaryMaximum,
    string? SalaryCurrency,
    string? SalaryPeriod,
    string? ExperienceRequirements,
    string? Description,
    IReadOnlyList<string> TechnologyStack,
    string? ApplicationEmail,
    string? ApplicationUrl,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> ConflictingFields);
public static class JobAnalysisAssessments
{
    public const string SingleJobPosting = "SINGLE_JOB_POSTING";
    public const string NotAJobPosting = "NOT_A_JOB_POSTING";
    public const string MultipleJobPostings = "MULTIPLE_JOB_POSTINGS";
    public const string UnreadableOrInsufficient = "UNREADABLE_OR_INSUFFICIENT";
}
public static class JobAnalysisContract
{
    public const string SchemaVersion = "1";
    public const string PromptVersion = "1";
}
public sealed record KnowledgeCollectionMember(string SourceType,Guid SourceRefId,string SourceKey,string ContentHash,int Ordinal);
public sealed record RetrievedKnowledge(Guid ChunkId,Guid DocumentId,string Title,string SourceType,Guid? SourceRefId,string? ProjectSlug,string Content,int Rank,decimal SimilarityScore);
