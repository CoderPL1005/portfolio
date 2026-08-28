using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Portfolio.Application.Common.Abstractions.AI;

namespace Portfolio.Infrastructure.AI;

public sealed class OpenAIEmbeddingService(IOptions<OpenAISettings> options):IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text,CancellationToken ct=default){var s=Require();var client=new EmbeddingClient(s.EmbeddingModel,s.ApiKey);var result=await client.GenerateEmbeddingAsync(text,new EmbeddingGenerationOptions{Dimensions=s.EmbeddingDimensions},ct);var values=result.Value.ToFloats().ToArray();if(values.Length!=s.EmbeddingDimensions)throw new InvalidOperationException($"Embedding provider returned {values.Length} dimensions; {s.EmbeddingDimensions} required.");return values;}
    private OpenAISettings Require(){var s=options.Value;if(string.IsNullOrWhiteSpace(s.ApiKey)||string.IsNullOrWhiteSpace(s.EmbeddingModel)||s.EmbeddingDimensions!=1536)throw new InvalidOperationException("OpenAI embedding configuration is unavailable or incompatible with vector(1536).");return s;}
}
public sealed class OpenAIChatCompletionService(IOptions<OpenAISettings> options):IChatCompletionService
{
    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest r,CancellationToken ct=default){var s=options.Value;if(string.IsNullOrWhiteSpace(s.ApiKey)||string.IsNullOrWhiteSpace(s.ChatModel))throw new InvalidOperationException("OpenAI chat configuration is unavailable.");var messages=new List<ChatMessage>{new SystemChatMessage(r.SystemInstructions)};messages.AddRange(r.History.Select(x=>x.Role=="USER"?(ChatMessage)new UserChatMessage(x.Content):new AssistantChatMessage(x.Content)));var context=string.Join("\n\n",r.Context.Select(x=>$"[Source {x.Rank}: {x.Title}]\n{x.Content}"));messages.Add(new UserChatMessage($"Portfolio context:\n{context}\n\nVisitor question:\n{r.UserMessage}"));var sw=Stopwatch.StartNew();var completion=await new ChatClient(s.ChatModel,s.ApiKey).CompleteChatAsync(messages,new ChatCompletionOptions{Temperature=(float)r.Temperature,MaxOutputTokenCount=r.MaxOutputTokens},ct);sw.Stop();var x=completion.Value;return new(string.Concat(x.Content.Select(p=>p.Text)).Trim(),x.Model,x.Usage?.InputTokenCount,x.Usage?.OutputTokenCount,(int)sw.ElapsedMilliseconds);}
}
