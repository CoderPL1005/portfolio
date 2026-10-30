using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Infrastructure.AI;

namespace Portfolio.UnitTests.PortfolioContent;

// Synthetic source/key/body only. No actual Gemini client transport is used.
public sealed class GeminiDiscoveryHttpContractTests
{
    private const string Source="SYNTHETIC_PRIVATE_RECRUITMENT_SOURCE";
    private const string Key="SYNTHETIC_PRIVATE_API_KEY";
    private static GeminiDiscoveryTextExtractor Extractor(Handler handler,Logs? logs=null)=>new(new HttpClient(handler),
        Options.Create(new GeminiSettings{ApiKey=Key,JobExtractionModel="gemini-3.1-flash-lite"}),logs);
    private static HttpResponseMessage Success()=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{
        candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text="{}"}}}}},usageMetadata=new{promptTokenCount=12,candidatesTokenCount=2}}),Encoding.UTF8,"application/json")};

    [Fact] public async Task Serialized_request_uses_supported_typed_schema_projection_without_changing_source()
    {
        JsonElement body=default;
        var handler=new Handler(async request=>{
            Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent",request.RequestUri!.ToString());
            Assert.Equal(Key,Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();return Success();
        });
        var result=await Extractor(handler).ExtractAsync(Source,default);
        Assert.Equal("{}",result.Output);Assert.Equal(1,handler.Calls);
        Assert.Equal(Source,body.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.True(body.TryGetProperty("systemInstruction",out _));
        var config=body.GetProperty("generationConfig");
        Assert.Equal("application/json",config.GetProperty("responseMimeType").GetString());Assert.Equal(8192,config.GetProperty("maxOutputTokens").GetInt32());
        Assert.False(config.TryGetProperty("responseSchema",out _));Assert.False(config.TryGetProperty("candidateCount",out _));
        var schema=config.GetProperty("responseJsonSchema");var version=schema.GetProperty("properties").GetProperty("schemaVersion");
        Assert.Equal("string",version.GetProperty("type").GetString());Assert.Equal("ai-job-extraction-v1",version.GetProperty("enum")[0].GetString());
        Check(schema);
        var nullable=schema.GetProperty("properties").GetProperty("employer").GetProperty("properties").GetProperty("value");
        Assert.Equal(new[]{"string","null"},nullable.GetProperty("type").EnumerateArray().Select(x=>x.GetString()));
        static void Check(JsonElement node){
            var allowed=new[]{"type","enum","properties","required","additionalProperties","items","anyOf"};
            Assert.All(node.EnumerateObject(),p=>Assert.Contains(p.Name,allowed));
            Assert.True(node.TryGetProperty("type",out _)||node.TryGetProperty("anyOf",out _),"Every branch must have a type or typed alternatives.");
            if(node.TryGetProperty("properties",out var properties))foreach(var p in properties.EnumerateObject())Check(p.Value);
            if(node.TryGetProperty("items",out var items))Check(items);
            if(node.TryGetProperty("anyOf",out var branches))foreach(var b in branches.EnumerateArray()){Assert.False(b.TryGetProperty("anyOf",out _));Check(b);}
        }
    }
    [Fact] public async Task Mock_schema_rejection_reproduces_400_until_const_and_constraint_complexity_are_projected()
    {
        var handler=new Handler(async request=>{
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var text=doc.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema").GetRawText();
            return text.Contains("\"const\"")||text.Contains("\"maxItems\"")?
                Error(400,"INVALID_ARGUMENT"):Success();
        });
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.Null(error);Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData(400,"INVALID_ARGUMENT","INVALID_ARGUMENT")]
    [InlineData(400,"FAILED_PRECONDITION","FAILED_PRECONDITION")]
    [InlineData(429,"RESOURCE_EXHAUSTED","RESOURCE_EXHAUSTED")]
    [InlineData(403,"PERMISSION_DENIED","PERMISSION_DENIED")]
    [InlineData(500,"INTERNAL","INTERNAL")]
    [InlineData(400,"UNTRUSTED_"+Source,"BAD_REQUEST")]
    public async Task Http_failure_preserves_only_allowlisted_metadata_and_never_retries(int status,string providerStatus,string category)
    {
        var handler=new Handler(_=>Task.FromResult(Error(status,providerStatus)));
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.NotNull(error);Assert.Equal(1,handler.Calls);
        Assert.Equal(status,error.Data["HttpStatus"]);Assert.Equal("HTTP_RESPONSE",error.Data["Stage"]);Assert.Equal(category,error.Data["Category"]);
        Assert.DoesNotContain(Source,error.ToString());Assert.DoesNotContain(Key,error.ToString());Assert.Null(error.InnerException);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Non_JSON_or_oversized_error_body_does_not_leak_or_guess_schema_failure(bool large)
    {
        var handler=new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent(large?new string('x',20000)+Source:Source+Key)}));
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.NotNull(error);Assert.Equal(400,error.Data["HttpStatus"]);Assert.Equal("BAD_REQUEST",error.Data["Category"]);Assert.Equal(1,handler.Calls);
        Assert.DoesNotContain(Source,error.ToString());Assert.DoesNotContain(Key,error.ToString());
    }
    [Fact] public async Task Transport_exception_details_never_escape_as_diagnostics()
    {
        var handler=new Handler(_=>throw new HttpRequestException(Source+Key));
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.NotNull(error);Assert.Equal("REQUEST_SEND",error.Data["Stage"]);Assert.Equal("TRANSPORT_UNKNOWN",error.Data["Category"]);
        Assert.Null(error.Data["HttpStatus"]);Assert.DoesNotContain(Source,error.ToString());Assert.DoesNotContain(Key,error.ToString());Assert.Null(error.InnerException);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task Malformed_success_is_sanitized_with_HTTP_200_and_parse_stage()
    {
        var handler=new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Source+Key)}));
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.NotNull(error);Assert.Equal(200,error.Data["HttpStatus"]);Assert.Equal("RESPONSE_PARSE",error.Data["Stage"]);Assert.Equal("MALFORMED_RESPONSE",error.Data["Category"]);
        Assert.DoesNotContain(Source,error.ToString());Assert.DoesNotContain(Key,error.ToString());Assert.Null(error.InnerException);
    }
    private static HttpResponseMessage Error(int status,string category)=>new((HttpStatusCode)status){Content=new StringContent(JsonSerializer.Serialize(new{error=new{status=category,message=Key+Source,details=new[]{new{privateValue=Source}}}}))};
    [Fact] public async Task Structured_log_contains_only_status_stage_and_category()
    {
        var logs=new Logs();var handler=new Handler(_=>Task.FromResult(Error(400,"FAILED_PRECONDITION")));
        await Record.ExceptionAsync(()=>Extractor(handler,logs).ExtractAsync(Source,default));
        var message=Assert.Single(logs.Messages);
        Assert.Contains("400",message);Assert.Contains("HTTP_RESPONSE",message);Assert.Contains("FAILED_PRECONDITION",message);
        Assert.DoesNotContain(Key,message);Assert.DoesNotContain(Source,message);Assert.DoesNotContain("message",message);Assert.DoesNotContain("details",message);
    }
    [Fact] public async Task Cancellation_is_unknown_and_never_automatically_retried()
    {
        var handler=new Handler(_=>throw new TaskCanceledException(Source+Key));
        var error=await Record.ExceptionAsync(()=>Extractor(handler).ExtractAsync(Source,default));
        Assert.NotNull(error);Assert.Equal("TIMEOUT",error.Data["Category"]);Assert.Null(error.Data["HttpStatus"]);Assert.Equal(1,handler.Calls);
        Assert.DoesNotContain(Source,error.ToString());Assert.DoesNotContain(Key,error.ToString());
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return send(request);}
    }
    private sealed class Logs:ILogger<GeminiDiscoveryTextExtractor>
    {
        public readonly List<string> Messages=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,exception));
    }
}
