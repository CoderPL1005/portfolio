using System.Net;
using System.Text;
using System.Text.Json;
using GeminiJdSmoke;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class GeminiJdSmokeTests
{
    private const string Secret = "SYNTHETIC_SECRET_DO_NOT_PRINT";
    private const string Model = "configured-smoke-model";
    private static Task<SmokeReport> Run(Handler handler, bool live = false, string? confirmation = null,
        bool interactive = true, int timeoutMs = 20000, TextWriter? notices = null) =>
        SmokeRunner.RunAsync(new(live, Secret, Model), new StringReader(confirmation ?? SmokeRunner.Confirmation),
            notices ?? TextWriter.Null, interactive, handler, timeoutMs);

    [Fact]
    public async Task Offline_default_invokes_actual_production_extractor_and_schema_with_no_real_network()
    {
        // No supplied transport: default must itself be offline and keyless.
        var result = await SmokeRunner.RunAsync(new(false, null, Model), TextReader.Null, TextWriter.Null, false);
        Assert.Equal("OFFLINE_MOCK", result.Mode);
        Assert.Equal("END_TO_END_PASS", result.EndToEnd);
        Assert.Equal(0, result.ProviderRequests);
        Assert.Equal(1, result.MockRequests);
        Assert.Equal(Model, result.ModelIdentity);
        Assert.Equal("VACANCY_FACTS_PRESENT_NOT_COMPLETENESS_PROOF", result.Coverage);
    }

    [Fact]
    public async Task Serialized_request_matches_production_contract_and_keeps_configured_model()
    {
        var handler = new Handler(async (request, _) => {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent", request.RequestUri!.ToString());
            Assert.DoesNotContain(Secret, request.RequestUri.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(SmokeRunner.Source, body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
            var config = body.RootElement.GetProperty("generationConfig");
            Assert.Equal(8192, config.GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
            var schema = config.GetProperty("responseJsonSchema");
            Assert.Equal("ai-job-extraction-v1", schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("enum")[0].GetString());
            Assert.DoesNotContain("\"const\"", schema.GetRawText());
            Assert.DoesNotContain("\"maxItems\"", schema.GetRawText());
            return Success();
        });
        var result = await Run(handler);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("TRANSPORT_PASS", result.Transport);
        Assert.Equal("JSON_PARSE_PASS", result.JsonParse);
        Assert.Equal("SCHEMA_PASS", result.Schema);
        Assert.Equal("EVIDENCE_PASS", result.Evidence);
        Assert.Equal("END_TO_END_PASS", result.EndToEnd);
        Assert.Equal("STOP", result.FinishReason);
        Assert.Equal(123, result.InputTokens);
        Assert.Equal(45, result.OutputTokens);
        Assert.Equal("ACME SOFTWARE", result.ValidatedSyntheticFields!.Value.GetProperty("employer").GetString());
        Assert.True(result.RequestDurationMs >= 0);
    }

    [Theory]
    [InlineData(false, "CONFIRM ONE REQUEST - PROJECT TIER VERIFIED")]
    [InlineData(true, "no")]
    public async Task Live_requires_explicit_interactive_confirmation_before_any_HTTP(bool interactive, string confirmation)
    {
        var handler = new Handler((_, _) => throw new Exception("HTTP_FORBIDDEN"));
        var notices = new StringWriter();
        var result = await Run(handler, true, confirmation, interactive, notices: notices);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, result.ProviderRequests);
        Assert.Equal("LIVE_CONSENT_REQUIRED", result.ErrorCategory);
        Assert.Contains("One Gemini API request may consume quota or incur charges. Confirm project tier before use.", notices.ToString());
    }

    [Theory]
    [InlineData(null, Model)]
    [InlineData(Secret, null)]
    [InlineData(Secret, "../unsafe")]
    public async Task Missing_or_invalid_live_configuration_fails_before_transport(string? key, string? model)
    {
        var handler = new Handler((_, _) => throw new Exception("HTTP_FORBIDDEN"));
        var result = await SmokeRunner.RunAsync(new(true, key, model), new StringReader(SmokeRunner.Confirmation), TextWriter.Null, true, handler);
        Assert.Equal("CONFIGURATION_INVALID", result.ErrorCategory);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(400, "INVALID_ARGUMENT", "INVALID_ARGUMENT")]
    [InlineData(429, "RESOURCE_EXHAUSTED", "RESOURCE_EXHAUSTED")]
    public async Task Provider_errors_are_safe_and_never_retry_or_fall_back(int status, string providerStatus, string category)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) {
            Content = new StringContent(JsonSerializer.Serialize(new { error = new { status = providerStatus, message = Secret,
                details = new[] { new { fieldViolations = new[] { new { field = "generation_config.response_json_schema", description = Secret } } } } } })) }));
        var result = await Run(handler);
        Assert.Equal(status, result.HttpStatus);
        Assert.Equal(category, result.ErrorCategory);
        Assert.Equal("HTTP_RESPONSE", result.FailureStage);
        Assert.True(result.ProviderResponseReceived);
        Assert.Equal("NOT_RUN", result.Evidence);
        Assert.Equal(1, handler.Calls);
        if (status == 400) Assert.Equal("SCHEMA_FIELD_REJECTED", result.ProviderErrorHint);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Timeout_is_reported_without_retry_or_raw_exception()
    {
        var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Success(); });
        var result = await Run(handler, timeoutMs: 30);
        Assert.Equal("TIMEOUT", result.ErrorCategory);
        Assert.False(result.ProviderResponseReceived);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("NOT_RUN", result.Schema);
    }

    [Theory]
    [InlineData("{}", "JSON_PARSE_PASS", "SCHEMA_FAIL")]
    [InlineData("not-json", "JSON_PARSE_FAIL", "NOT_RUN")]
    public async Task Http_200_is_not_extraction_success(string output, string parse, string schema)
    {
        var result = await Run(new Handler((_, _) => Task.FromResult(Success(output))));
        Assert.Equal("TRANSPORT_PASS", result.Transport);
        Assert.Equal(parse, result.JsonParse);
        Assert.Equal(schema, result.Schema);
        Assert.NotEqual("END_TO_END_PASS", result.EndToEnd);
    }

    [Fact]
    public async Task Unsupported_fact_is_rejected_by_existing_evidence_validator()
    {
        var payload = Payload();
        payload["employer"] = Fact("INVENTED COMPANY", "ACME SOFTWARE");
        var result = await Run(new Handler((_, _) => Task.FromResult(Success(JsonSerializer.Serialize(payload)))));
        Assert.Equal("SCHEMA_PASS", result.Schema);
        Assert.Equal("EVIDENCE_PARTIAL", result.Evidence);
        Assert.NotEqual("END_TO_END_PASS", result.EndToEnd);
        Assert.Equal(JsonValueKind.Null, result.ValidatedSyntheticFields!.Value.GetProperty("employer").ValueKind);
        Assert.DoesNotContain("INVENTED COMPANY", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Consented_transport_is_one_call_and_no_followup_for_incomplete_response()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}],\"usageMetadata\":{\"promptTokenCount\":20,\"candidatesTokenCount\":8192}}") }));
        var result = await Run(handler, live: true);
        Assert.Equal("LIVE", result.Mode);
        Assert.Equal("OUTPUT_INCOMPLETE", result.ErrorCategory);
        Assert.Equal(1, result.GenerateContentAttempts);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("MAX_TOKENS", result.FinishReason);
        Assert.Equal(20, result.InputTokens);
        Assert.Equal(8192, result.OutputTokens);
    }

    [Fact]
    public async Task Redirect_or_malformed_outer_response_cannot_trigger_another_call_or_leak_content()
    {
        foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.OK })
        {
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) {
                Headers = { Location = new Uri("https://untrusted.invalid/") }, Content = new StringContent(Secret) }));
            var result = await Run(handler);
            Assert.Equal(1, handler.Calls);
            Assert.NotEqual("END_TO_END_PASS", result.EndToEnd);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
            Assert.Equal(status == HttpStatusCode.OK ? "MALFORMED_RESPONSE" : "HTTP_REJECTED", result.ErrorCategory);
        }
    }

    [Fact]
    public async Task Explicit_inline_recruitment_title_is_validated_without_reformatting_source()
    {
        var handler = new Handler((_, _) => Task.FromResult(Success(JsonSerializer.Serialize(Payload(true)))));
        var result = await Run(handler);
        Assert.Equal("SCHEMA_PASS", result.Schema);
        Assert.Equal("EVIDENCE_PASS", result.Evidence);
        Assert.Equal("END_TO_END_PASS", result.EndToEnd);
        var vacancy = Assert.Single(result.ValidatedSyntheticFields!.Value.GetProperty("vacancies").EnumerateArray());
        Assert.Equal("Intern Backend Developer", vacancy.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("generation_config.response_json_schema", "SCHEMA_FIELD_REJECTED")]
    [InlineData("generation_config.max_output_tokens", "PARAMETER_FIELD_REJECTED")]
    [InlineData("untrusted-private-field", "UNSPECIFIED")]
    public async Task Bad_request_diagnostic_only_projects_fixed_known_field_categories(string field, string hint)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) {
            Content = new StringContent(JsonSerializer.Serialize(new { error = new { status = "INVALID_ARGUMENT", message = Secret,
                details = new[] { new { fieldViolations = new[] { new { field, description = Secret } } } } } })) }));
        var result = await Run(handler);
        Assert.Equal(hint, result.ProviderErrorHint);
        Assert.Equal("INVALID_ARGUMENT", result.ErrorCategory);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        Assert.DoesNotContain("untrusted-private-field", JsonSerializer.Serialize(result));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task All_unknown_response_cannot_establish_end_to_end_extraction()
    {
        var payload = Payload(); payload["employer"] = Unknown(); payload["sharedLocation"] = Unknown();
        var result = await Run(new Handler((_, _) => Task.FromResult(Success(JsonSerializer.Serialize(payload)))));
        Assert.Equal("SCHEMA_PASS", result.Schema);
        Assert.Equal("EVIDENCE_ABSTAINED", result.Evidence);
        Assert.Equal("NO_SUPPORTED_FACTS", result.Coverage);
        Assert.Equal("END_TO_END_INCOMPLETE", result.EndToEnd);
    }

    [Fact]
    public async Task Arbitrary_response_metadata_and_accidental_key_as_model_are_not_echoed()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new { modelVersion = Secret, candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = JsonSerializer.Serialize(Payload()) } } } } } })) }));
        var result = await Run(handler);
        Assert.Null(result.ResponseModelIdentity);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        var unused = new Handler((_, _) => throw new Exception("HTTP_FORBIDDEN"));
        var invalid = await SmokeRunner.RunAsync(new(true, Secret, Secret), new StringReader(SmokeRunner.Confirmation), TextWriter.Null, true, unused);
        Assert.Equal("CONFIGURATION_INVALID", invalid.ErrorCategory);
        Assert.Equal(0, unused.Calls);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(invalid));
    }

    internal static Dictionary<string, object> Payload(bool includeInlineVacancy = false)
    {
        var post = new Dictionary<string, object> { ["schemaVersion"] = "ai-job-extraction-v1" };
        foreach (var name in new[] { "employer", "recruitmentProgram", "sharedLocation", "sharedRequirements", "sharedBenefits", "applicationUrl", "recruitmentStatus", "publishedAt" }) post[name] = Unknown();
        post["employer"] = Fact("ACME SOFTWARE", "ACME SOFTWARE");
        post["sharedLocation"] = Fact("Hà Nội", "Hà Nội");
        var vacancy = new Dictionary<string, object>();
        foreach (var name in new[] { "title", "roleCategory", "seniority", "technologies", "mandatorySkills", "optionalSkills", "requirements", "experienceYears", "salary", "location", "applicationUrl" }) vacancy[name] = Unknown();
        vacancy["title"] = Fact("Intern Backend Developer", "Intern Backend Developer");
        vacancy["roleCategory"] = Fact("BACKEND", "Backend");
        vacancy["seniority"] = Fact("INTERN", "Intern");
        vacancy["technologies"] = Fact(new[] { "C#", "ASP.NET Core", "SQL Server" }, "Công nghệ: C#, ASP.NET Core, SQL Server.");
        // Strict default tests remain separate. Smoke V2 opts into explicit
        // recruitment title recognition without modifying the source text.
        post["vacancies"] = includeInlineVacancy ? new[] { vacancy } : Array.Empty<Dictionary<string, object>>();
        return post;
    }
    private static object Unknown() => new { value = (string?)null, status = "UNKNOWN", evidence = Array.Empty<object>() };
    private static object Fact(object value, string quote) { var start = SmokeRunner.Source.IndexOf(quote, StringComparison.Ordinal); return new { value, status = "POST_MENTIONED", evidence = new[] { new { start, end = start + quote.Length, quote } } }; }
    private static HttpResponseMessage Success(string? output = null) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output ?? JsonSerializer.Serialize(Payload(true)) } } } } },
            usageMetadata = new { promptTokenCount = 123, candidatesTokenCount = 45 } }), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request, ct); }
    }
}
