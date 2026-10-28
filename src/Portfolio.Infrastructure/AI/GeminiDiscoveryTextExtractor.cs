using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Infrastructure.AI;

// Uses existing Gemini project/key, never a second key or chatbot behavior change.
public sealed class GeminiDiscoveryTextExtractor(HttpClient http,IOptions<GeminiSettings> options):IDiscoveryTextExtractor
{
    private static readonly JsonElement Contract=LoadContract();
    private static JsonElement LoadContract()
    {
        using var stream=typeof(GeminiDiscoveryTextExtractor).Assembly.GetManifestResourceStream("Portfolio.Infrastructure.AI.discovery-extraction-contract.json")!;
        using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();
    }
    public string ModelIdentity=>string.IsNullOrWhiteSpace(options.Value.JobExtractionModel)?"unconfigured":options.Value.JobExtractionModel.Replace("models/","",StringComparison.Ordinal);
    public async Task<DiscoveryTextGeneration> ExtractAsync(string rawContent,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(options.Value.ApiKey)||ModelIdentity=="unconfigured"||rawContent.Length>24000||string.IsNullOrWhiteSpace(rawContent))throw new InvalidOperationException("DISCOVERY_SEMANTIC_UNAVAILABLE");
        using var request=new HttpRequestMessage(HttpMethod.Post,$"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(ModelIdentity)}:generateContent");
        request.Headers.Add("x-goog-api-key",options.Value.ApiKey);
        request.Content=JsonContent.Create(new{
            systemInstruction=new{parts=new[]{new{text=Contract.GetProperty("instructions").GetString()}}},
            contents=new[]{new{role="user",parts=new[]{new{text=rawContent}}}},
            generationConfig=new{temperature=0,maxOutputTokens=8192,responseMimeType="application/json",responseJsonSchema=Contract.GetProperty("schema")}
        });
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("DISCOVERY_SEMANTIC_UNAVAILABLE");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[8192];int n;
        while((n=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+n>128*1024)throw new InvalidOperationException("DISCOVERY_SEMANTIC_OUTPUT_TOO_LARGE");buffer.Write(chunk,0,n);}
        using var document=JsonDocument.Parse(buffer.ToArray());var root=document.RootElement;
        var candidates=root.GetProperty("candidates");if(candidates.GetArrayLength()!=1)throw new InvalidOperationException("DISCOVERY_SEMANTIC_INVALID");
        var candidate=candidates[0];if(candidate.GetProperty("finishReason").GetString()!="STOP")throw new InvalidOperationException("DISCOVERY_SEMANTIC_INCOMPLETE");
        var parts=candidate.GetProperty("content").GetProperty("parts");if(parts.GetArrayLength()!=1)throw new InvalidOperationException("DISCOVERY_SEMANTIC_INVALID");
        var output=parts[0].GetProperty("text").GetString()!;if(System.Text.Encoding.UTF8.GetByteCount(output)>65536)throw new InvalidOperationException("DISCOVERY_SEMANTIC_OUTPUT_TOO_LARGE");
        int? Tokens(string key)=>root.TryGetProperty("usageMetadata",out var usage)&&usage.TryGetProperty(key,out var count)&&count.TryGetInt32(out var number)?number:null;
        return new(ModelIdentity,"ai-job-extraction-v1/ai-extraction-prompt-v1",output,Tokens("promptTokenCount"),Tokens("candidatesTokenCount"));
    }
}
