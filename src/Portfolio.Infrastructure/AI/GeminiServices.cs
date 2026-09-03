using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.AI;

namespace Portfolio.Infrastructure.AI;

public sealed class GeminiEmbeddingService(
    HttpClient httpClient,
    IOptions<GeminiSettings> options) : IEmbeddingService
{
    private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/";

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var model = NormalizeModel(settings.EmbeddingModel);
        using var request = CreateRequest(
            $"models/{Uri.EscapeDataString(model)}:embedContent",
            settings.ApiKey,
            new
            {
                model = $"models/{model}",
                content = new { parts = new[] { new { text } } },
                output_dimensionality = settings.EmbeddingDimensions
            });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!payload.RootElement.TryGetProperty("embedding", out var embedding)
            || !embedding.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Gemini embedding response was invalid.");
        }

        var result = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (result.Length != settings.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned {result.Length} dimensions; {settings.EmbeddingDimensions} required.");
        }

        return result;
    }

    private GeminiSettings RequireSettings()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.EmbeddingModel)
            || settings.EmbeddingDimensions != 1536)
        {
            throw new InvalidOperationException(
                "Gemini embedding configuration is unavailable or incompatible with vector(1536).");
        }

        return settings;
    }

    internal static HttpRequestMessage CreateRequest(string relativePath, string apiKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ApiBaseUrl), relativePath))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        return request;
    }

    internal static string NormalizeModel(string model) =>
        model.Trim().StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model.Trim()["models/".Length..]
            : model.Trim();

    internal static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            $"Gemini provider request failed with HTTP {(int)response.StatusCode}.",
            inner: null,
            response.StatusCode);
    }
}

public sealed class GeminiChatCompletionService(
    HttpClient httpClient,
    IOptions<GeminiSettings> options) : IChatCompletionService
{
    public async Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ChatModel))
        {
            throw new InvalidOperationException("Gemini chat configuration is unavailable.");
        }

        var contents = request.History
            .Select(item => new GeminiContent(
                item.Role == "USER" ? "user" : "model",
                [new GeminiPart(item.Content)]))
            .ToList();
        var context = string.Join(
            "\n\n",
            request.Context.Select(item => $"[Source {item.Rank}: {item.Title}]\n{item.Content}"));
        contents.Add(new GeminiContent(
            "user",
            [new GeminiPart($"Portfolio context:\n{context}\n\nVisitor question:\n{request.UserMessage}")]));

        var model = GeminiEmbeddingService.NormalizeModel(settings.ChatModel);
        using var providerRequest = GeminiEmbeddingService.CreateRequest(
            $"models/{Uri.EscapeDataString(model)}:generateContent",
            settings.ApiKey,
            new
            {
                systemInstruction = new { parts = new[] { new GeminiPart(request.SystemInstructions) } },
                contents,
                generationConfig = new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxOutputTokens
                }
            });

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(providerRequest, cancellationToken);
        GeminiEmbeddingService.EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        stopwatch.Stop();

        var content = ReadContent(payload.RootElement);
        var modelName = ReadString(payload.RootElement, "modelVersion") ?? settings.ChatModel;
        var promptTokens = ReadInt32(payload.RootElement, "usageMetadata", "promptTokenCount");
        var completionTokens = ReadInt32(payload.RootElement, "usageMetadata", "candidatesTokenCount");
        return new ChatCompletionResult(
            content,
            modelName,
            promptTokens,
            completionTokens,
            (int)stopwatch.ElapsedMilliseconds);
    }

    private static string ReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Gemini chat response did not contain a completion.");
        }

        var result = string.Concat(parts.EnumerateArray()
            .Select(part => ReadString(part, "text") ?? string.Empty)).Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException("Gemini chat response did not contain a completion.");
        }

        return result;
    }

    private static int? ReadInt32(JsonElement root, string parentName, string propertyName) =>
        root.TryGetProperty(parentName, out var parent)
        && parent.TryGetProperty(propertyName, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record GeminiContent(string Role, IReadOnlyCollection<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
}
