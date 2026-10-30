using System.Net;
using System.Text;
using System.Text.Json;

namespace GeminiJdSmoke;

// Harness-only observation. Production serializes every request and parses the
// same bounded response; no alternate Gemini request or extraction implementation.
internal sealed class ObservedTransport(HttpMessageHandler inner, string model) : DelegatingHandler(inner)
{
    public int Calls { get; private set; }
    public int? Status { get; private set; }
    public string? FinishReason { get; private set; }
    public string? ResponseModelIdentity { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public string ErrorHint { get; private set; } = "UNSPECIFIED";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Calls != 0 || request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri !=
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
            throw new HttpRequestException("SMOKE_REQUEST_BOUNDARY_REJECTED");
        Calls++;
        var response = await base.SendAsync(request, ct);
        Status = (int)response.StatusCode;
        try
        {
            var original = response.Content;
            var limit = response.IsSuccessStatusCode ? 128 * 1024 : 16 * 1024;
            await using var stream = await original.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (buffer.Length <= limit)
            {
                var n = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit + 1 - buffer.Length)), ct);
                if (n == 0) break;
                buffer.Write(chunk, 0, n);
            }
            var bytes = buffer.ToArray();
            var content = new ByteArrayContent(bytes);
            foreach (var header in original.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = content;
            original.Dispose();
            if (bytes.Length <= limit) Inspect(bytes);
            return response;
        }
        catch { response.Dispose(); throw; }
    }
    private void Inspect(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            // Only echo the already validated requested identity. An arbitrary
            // provider modelVersion string is not an approved diagnostic value.
            if (root.TryGetProperty("modelVersion", out var version) && version.ValueKind == JsonValueKind.String &&
                version.GetString() == model) ResponseModelIdentity = model;
            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                InputTokens = Tokens("promptTokenCount"); OutputTokens = Tokens("candidatesTokenCount");
                int? Tokens(string name) => usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var n) && n >= 0 ? n : null;
            }
            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() == 1 &&
                candidates[0].TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String &&
                reason.GetString() is "STOP" or "MAX_TOKENS" or "SAFETY" or "RECITATION" or "OTHER" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "MALFORMED_FUNCTION_CALL")
                FinishReason = reason.GetString();
            if (Status == 400 && root.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var detail in details.EnumerateArray())
                    if (detail.TryGetProperty("fieldViolations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                        foreach (var violation in violations.EnumerateArray())
                            if (violation.TryGetProperty("field", out var field) && field.ValueKind == JsonValueKind.String)
                            {
                                // Exact trusted field names only, never error.message/description.
                                if (field.GetString() is "generation_config.response_json_schema" or "generationConfig.responseJsonSchema") ErrorHint = "SCHEMA_FIELD_REJECTED";
                                else if (ErrorHint == "UNSPECIFIED" && field.GetString() is "generation_config.max_output_tokens" or "generationConfig.maxOutputTokens" or "contents") ErrorHint = "PARAMETER_FIELD_REJECTED";
                            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
    }
}

internal sealed class OfflineTransport : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(request.Content!.ReadAsStream(ct));
        var root = doc.RootElement;
        var config = root.GetProperty("generationConfig");
        var schema = config.GetProperty("responseJsonSchema");
        if (root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() != SmokeRunner.Source ||
            config.GetProperty("responseMimeType").GetString() != "application/json" || config.GetProperty("maxOutputTokens").GetInt32() != 8192 ||
            schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("enum")[0].GetString() != "ai-job-extraction-v1" ||
            schema.GetRawText().Contains("\"const\"", StringComparison.Ordinal)) throw new HttpRequestException("OFFLINE_CONTRACT_INVALID");
        var payload = new Dictionary<string, object> { ["schemaVersion"] = "ai-job-extraction-v1", ["vacancies"] = Array.Empty<object>() };
        foreach (var field in new[] { "employer", "recruitmentProgram", "sharedLocation", "sharedRequirements", "sharedBenefits", "applicationUrl", "recruitmentStatus", "publishedAt" })
            payload[field] = new { value = (string?)null, status = "UNKNOWN", evidence = Array.Empty<object>() };
        payload["employer"] = Fact("ACME SOFTWARE"); payload["sharedLocation"] = Fact("Hà Nội");
        var vacancy = new Dictionary<string, object>();
        foreach (var field in new[] { "title", "roleCategory", "seniority", "technologies", "mandatorySkills", "optionalSkills", "requirements", "experienceYears", "salary", "location", "applicationUrl" })
            vacancy[field] = new { value = (string?)null, status = "UNKNOWN", evidence = Array.Empty<object>() };
        vacancy["title"] = Fact("Intern Backend Developer");
        vacancy["roleCategory"] = Fact("BACKEND", "Backend"); vacancy["seniority"] = Fact("INTERN", "Intern");
        vacancy["technologies"] = Fact(new[] { "C#", "ASP.NET Core", "SQL Server" }, "Công nghệ: C#, ASP.NET Core, SQL Server.");
        vacancy["requirements"] = Fact("Có kiến thức C# và REST API.", "Yêu cầu: Có kiến thức C# và REST API.");
        payload["vacancies"] = new[] { vacancy };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
            candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = JsonSerializer.Serialize(payload) } } } } },
            usageMetadata = new { promptTokenCount = 123, candidatesTokenCount = 45 }
        }), Encoding.UTF8, "application/json") });
    }
    private static object Fact(string quote) => Fact(quote, quote);
    private static object Fact(object value, string quote) { var start = SmokeRunner.Source.IndexOf(quote, StringComparison.Ordinal); return new { value, status = "POST_MENTIONED", evidence = new[] { new { start, end = start + quote.Length, quote } } }; }
}
