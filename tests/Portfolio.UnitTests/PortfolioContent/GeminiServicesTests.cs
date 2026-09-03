using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Infrastructure.AI;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class GeminiServicesTests
{
    [Fact]
    public async Task Embedding_requires_api_key_model_and_vector_1536_compatibility()
    {
        foreach (var settings in new[]
        {
            Settings(apiKey: ""),
            Settings(embeddingModel: ""),
            Settings(embeddingDimensions: 768)
        })
        {
            var service = new GeminiEmbeddingService(
                new HttpClient(new StubHandler(_ => Success("{}"))),
                Options.Create(settings));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateEmbeddingAsync("portfolio"));
        }
    }

    [Fact]
    public async Task Chat_requires_api_key_and_chat_model()
    {
        foreach (var settings in new[] { Settings(apiKey: ""), Settings(chatModel: "") })
        {
            var service = new GeminiChatCompletionService(
                new HttpClient(new StubHandler(_ => Success("{}"))),
                Options.Create(settings));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CompleteAsync(ChatRequest()));
        }
    }

    [Fact]
    public async Task Embedding_requests_1536_dimensions_and_rejects_wrong_response_length()
    {
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return Success("{\"embedding\":{\"values\":[0.1,0.2,0.3]}}");
        });
        var service = new GeminiEmbeddingService(
            new HttpClient(handler),
            Options.Create(Settings()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("portfolio"));

        Assert.Contains("3 dimensions; 1536 required", error.Message);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(1536, body.RootElement.GetProperty("output_dimensionality").GetInt32());
        Assert.Equal("models/gemini-embedding-2", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Embedding_returns_configured_1536_dimension_vector()
    {
        var values = string.Join(',', Enumerable.Repeat("0.25", 1536));
        var service = new GeminiEmbeddingService(
            new HttpClient(new StubHandler(_ =>
                Success($"{{\"embedding\":{{\"values\":[{values}]}}}}"))),
            Options.Create(Settings()));

        var result = await service.GenerateEmbeddingAsync("portfolio");

        Assert.Equal(1536, result.Length);
    }

    [Fact]
    public async Task Chat_preserves_system_history_context_user_message_and_output_limit()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return Success("""
                {
                  "candidates": [{"content": {"parts": [{"text": "Grounded answer"}]}}],
                  "usageMetadata": {"promptTokenCount": 21, "candidatesTokenCount": 7},
                  "modelVersion": "gemini-3.1-flash-lite-001"
                }
                """);
        });
        var service = new GeminiChatCompletionService(
            new HttpClient(handler),
            Options.Create(Settings()));

        var result = await service.CompleteAsync(ChatRequest());

        Assert.Equal("Grounded answer", result.Content);
        Assert.Equal("gemini-3.1-flash-lite-001", result.ModelName);
        Assert.Equal(21, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);
        Assert.NotNull(result.LatencyMs);
        Assert.Equal("test-only-key", capturedRequest!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("test-only-key", capturedRequest.RequestUri!.ToString());

        using var body = JsonDocument.Parse(capturedBody!);
        var root = body.RootElement;
        Assert.Equal(
            "Stay within portfolio facts.",
            root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var contents = root.GetProperty("contents");
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal("Previous question", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("Previous answer", contents[1].GetProperty("parts")[0].GetProperty("text").GetString());
        var finalPrompt = contents[2].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Contains("[Source 1: SchoolSaaS]", finalPrompt);
        Assert.Contains("Verified portfolio context", finalPrompt);
        Assert.Contains("Visitor question:\nWhat did they build?", finalPrompt);
        Assert.Equal(0.25m, root.GetProperty("generationConfig").GetProperty("temperature").GetDecimal());
        Assert.Equal(600, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task Provider_failure_does_not_expose_api_key_or_response_body()
    {
        const string secret = "sensitive-test-key";
        var service = new GeminiChatCompletionService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent($"provider detail containing {secret}")
            })),
            Options.Create(Settings(apiKey: secret)));

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.CompleteAsync(ChatRequest()));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.DoesNotContain(secret, error.ToString());
        Assert.DoesNotContain("provider detail", error.ToString());
    }

    private static GeminiSettings Settings(
        string apiKey = "test-only-key",
        string chatModel = "gemini-3.1-flash-lite",
        string embeddingModel = "gemini-embedding-2",
        int embeddingDimensions = 1536) => new()
        {
            ApiKey = apiKey,
            ChatModel = chatModel,
            EmbeddingModel = embeddingModel,
            EmbeddingDimensions = embeddingDimensions
        };

    private static ChatCompletionRequest ChatRequest() => new(
        "Stay within portfolio facts.",
        "What did they build?",
        [new("USER", "Previous question"), new("ASSISTANT", "Previous answer")],
        [new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SchoolSaaS",
            "PROJECT",
            Guid.NewGuid(),
            "school-saas",
            "Verified portfolio context",
            1,
            0.9m)],
        0.25m,
        600);

    private static HttpResponseMessage Success(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this(request => Task.FromResult(response(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }
}
