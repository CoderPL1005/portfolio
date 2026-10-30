using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Infrastructure.AI;

// Uses existing Gemini project/key, never a second key or chatbot behavior change.
public sealed class GeminiDiscoveryTextExtractor(HttpClient http,IOptions<GeminiSettings> options,ILogger<GeminiDiscoveryTextExtractor>? logger=null):IDiscoveryTextExtractor
{
    private static readonly JsonElement Contract=LoadContract();
    private static readonly JsonElement ProviderSchema=GeminiDiscoverySchema.Project(Contract.GetProperty("schema"));
    private static JsonElement LoadContract()
    {
        using var stream=typeof(GeminiDiscoveryTextExtractor).Assembly.GetManifestResourceStream("Portfolio.Infrastructure.AI.discovery-extraction-contract.json")!;
        using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();
    }
    public string ModelIdentity=>string.IsNullOrWhiteSpace(options.Value.JobExtractionModel)?"unconfigured":options.Value.JobExtractionModel.Replace("models/","",StringComparison.Ordinal);
    public bool IsConfigured=>!string.IsNullOrWhiteSpace(options.Value.ApiKey)&&ModelIdentity!="unconfigured"&&System.Text.RegularExpressions.Regex.IsMatch(ModelIdentity,"^[A-Za-z0-9._-]{1,160}$");
    public async Task<DiscoveryTextGeneration> ExtractAsync(string rawContent,CancellationToken ct)
    {
        if(!IsConfigured||rawContent.Length>24000||string.IsNullOrWhiteSpace(rawContent))throw new InvalidOperationException("DISCOVERY_SEMANTIC_UNAVAILABLE");
        using var request=new HttpRequestMessage(HttpMethod.Post,$"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(ModelIdentity)}:generateContent");
        request.Headers.Add("x-goog-api-key",options.Value.ApiKey);
        request.Content=JsonContent.Create(new{
            systemInstruction=new{parts=new[]{new{text=Contract.GetProperty("instructions").GetString()}}},
            contents=new[]{new{role="user",parts=new[]{new{text=rawContent}}}},
            generationConfig=new{temperature=0,maxOutputTokens=8192,responseMimeType="application/json",responseJsonSchema=ProviderSchema}
        });
        int? status=null;var stage=DiscoveryExtractionStage.REQUEST_SEND;
        try{
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            status=(int)response.StatusCode;stage=DiscoveryExtractionStage.HTTP_RESPONSE;
            if(!response.IsSuccessStatusCode)throw Failure(status,stage,await ErrorCategory(response,ct));
            stage=DiscoveryExtractionStage.RESPONSE_PARSE;
            var bytes=await ReadBounded(response,128*1024,ct);
            if(bytes is null)throw Failure(status,stage,DiscoveryExtractionCategory.OUTPUT_TOO_LARGE);
            using var document=JsonDocument.Parse(bytes);var root=document.RootElement;
            stage=DiscoveryExtractionStage.RESPONSE_VALIDATION;
            var candidates=root.GetProperty("candidates");
            if(candidates.ValueKind!=JsonValueKind.Array||candidates.GetArrayLength()!=1)throw Failure(status,stage,DiscoveryExtractionCategory.MALFORMED_RESPONSE);
            var candidate=candidates[0];
            if(candidate.GetProperty("finishReason").GetString()!="STOP")throw Failure(status,stage,DiscoveryExtractionCategory.OUTPUT_INCOMPLETE);
            var parts=candidate.GetProperty("content").GetProperty("parts");
            if(parts.ValueKind!=JsonValueKind.Array||parts.GetArrayLength()!=1)throw Failure(status,stage,DiscoveryExtractionCategory.MALFORMED_RESPONSE);
            var output=parts[0].GetProperty("text").GetString();
            if(string.IsNullOrWhiteSpace(output))throw Failure(status,stage,DiscoveryExtractionCategory.MALFORMED_RESPONSE);
            if(System.Text.Encoding.UTF8.GetByteCount(output)>65536)throw Failure(status,stage,DiscoveryExtractionCategory.OUTPUT_TOO_LARGE);
            int? Tokens(string key)=>root.TryGetProperty("usageMetadata",out var usage)&&usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty(key,out var count)&&count.ValueKind==JsonValueKind.Number&&count.TryGetInt32(out var number)&&number>=0?number:null;
            return new(ModelIdentity,"ai-job-extraction-v1/ai-extraction-prompt-v1",output,Tokens("promptTokenCount"),Tokens("candidatesTokenCount"));
        }
        catch(DiscoveryTextExtractionException){throw;}
        catch(OperationCanceledException){throw Failure(status,stage,DiscoveryExtractionCategory.TIMEOUT);}
        catch(HttpRequestException){throw Failure(status,stage,DiscoveryExtractionCategory.TRANSPORT_UNKNOWN);}
        catch(IOException){throw Failure(status,stage,DiscoveryExtractionCategory.TRANSPORT_UNKNOWN);}
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException){throw Failure(status,stage,DiscoveryExtractionCategory.MALFORMED_RESPONSE);}
    }
    private DiscoveryTextExtractionException Failure(int? status,DiscoveryExtractionStage stage,DiscoveryExtractionCategory category)
    {
        logger?.LogWarning("Gemini JD extraction failed: HTTP {HttpStatus}; stage {Stage}; category {Category}.",status,stage.ToString(),category.ToString());
        return new(status,stage,category);
    }
    private static async Task<byte[]?> ReadBounded(HttpResponseMessage response,int limit,CancellationToken ct)
    {
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[4096];int n;
        while((n=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+n>limit)return null;buffer.Write(chunk,0,n);}
        return buffer.ToArray();
    }
    private static async Task<DiscoveryExtractionCategory> ErrorCategory(HttpResponseMessage response,CancellationToken ct)
    {
        var status=(int)response.StatusCode;
        var fallback=status switch{400=>DiscoveryExtractionCategory.BAD_REQUEST,401=>DiscoveryExtractionCategory.UNAUTHENTICATED,
            403=>DiscoveryExtractionCategory.PERMISSION_DENIED,404=>DiscoveryExtractionCategory.NOT_FOUND,429=>DiscoveryExtractionCategory.RESOURCE_EXHAUSTED,
            500=>DiscoveryExtractionCategory.INTERNAL,503=>DiscoveryExtractionCategory.UNAVAILABLE,_=>DiscoveryExtractionCategory.HTTP_REJECTED};
        try{
            var bytes=await ReadBounded(response,16*1024,ct);if(bytes is null)return fallback;
            using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("error",out var error)||error.ValueKind!=JsonValueKind.Object||
                !error.TryGetProperty("status",out var value)||value.ValueKind!=JsonValueKind.String)return fallback;
            // In particular, 400 can be billing/region FAILED_PRECONDITION, not
            // necessarily schema INVALID_ARGUMENT. Never retain error.message.
            return (status,value.GetString()) switch{
                (400,"INVALID_ARGUMENT")=>DiscoveryExtractionCategory.INVALID_ARGUMENT,
                (400,"FAILED_PRECONDITION")=>DiscoveryExtractionCategory.FAILED_PRECONDITION,_=>fallback};
        }catch(Exception e) when(e is JsonException or HttpRequestException or IOException or OperationCanceledException){return fallback;}
    }
}
