using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Portfolio.Infrastructure.AI;
using Portfolio.Application.Features.JobHunting;

namespace GeminiJdSmoke;

public sealed record SmokeOptions(bool Live, string? ApiKey, string? Model)
{
    public override string ToString() => "SmokeOptions [REDACTED]";
}
public sealed record SmokeReport
{
    public string Mode { get; init; } = "OFFLINE_MOCK";
    public string? ModelIdentity { get; init; }
    public string? ResponseModelIdentity { get; init; }
    public int GenerateContentAttempts { get; init; }
    public int ProviderRequests { get; init; }
    public int MockRequests { get; init; }
    public int? HttpStatus { get; init; }
    public bool ProviderResponseReceived { get; init; }
    public long RequestDurationMs { get; init; }
    public string? FinishReason { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string Transport { get; init; } = "NOT_RUN";
    public string JsonParse { get; init; } = "NOT_RUN";
    public string Schema { get; init; } = "NOT_RUN";
    public string Evidence { get; init; } = "NOT_RUN";
    public string EndToEnd { get; init; } = "NOT_RUN";
    public string? ErrorCategory { get; init; }
    public string? FailureStage { get; init; }
    public string? DiagnosticId { get; init; }
    public string ProviderErrorHint { get; init; } = "UNSPECIFIED";
    public string Coverage { get; init; } = "NOT_EVALUATED";
    public string EvidencePolicy { get; init; } = "SMOKE_OPT_IN_V2";
    public JsonElement? EvidenceDiagnostics { get; init; }
    public JsonElement? MissingFacts { get; init; }
    public JsonElement? ValidatedSyntheticFields { get; init; }
}

public static class SmokeRunner
{
    public const string Source = "ACME SOFTWARE tuyển dụng Intern Backend Developer\n(.NET) tại Hà Nội.\n\nCông nghệ: C#, ASP.NET Core, SQL Server.\n\nYêu cầu: Có kiến thức C# và REST API.\n\nĐịa điểm: Hà Nội.\n\nỨng tuyển theo hướng dẫn của công ty.";
    public const string Confirmation = "CONFIRM ONE REQUEST - PROJECT TIER VERIFIED";
    public static async Task<SmokeReport> RunAsync(SmokeOptions options, TextReader input, TextWriter notices,
        bool interactive, HttpMessageHandler? transport = null, int timeoutMs = 20000)
    {
        var model = options.Model ?? (options.Live ? "unconfigured" : "synthetic-offline-model");
        // Use the actual production configuration validation, without any host/DI/DB.
        var settings = Options.Create(new GeminiSettings { ApiKey = options.Live ? options.ApiKey ?? "" : "synthetic-offline-key", JobExtractionModel = model });
        using var unavailable = new HttpClient(new OfflineTransport());
        var configured = new GeminiDiscoveryTextExtractor(unavailable, settings);
        var report = new SmokeReport { Mode = options.Live ? "LIVE" : "OFFLINE_MOCK", ModelIdentity = configured.IsConfigured ? configured.ModelIdentity : null };
        if (!configured.IsConfigured || timeoutMs is < 1 or > 20000 || options.Live && configured.ModelIdentity == options.ApiKey)
            return report with { ModelIdentity = null, ErrorCategory = "CONFIGURATION_INVALID", FailureStage = "PREFLIGHT" };
        if (options.Live)
        {
            await notices.WriteLineAsync("One Gemini API request may consume quota or incur charges. Confirm project tier before use.");
            await notices.WriteLineAsync($"Model: {configured.ModelIdentity}. Maximum: ONE generateContent request, 8192 output tokens, 20 seconds. No retries/fallback. No free-tier assumption.");
            await notices.WriteLineAsync($"After verifying your existing project's tier/billing, type exactly: {Confirmation}");
            if (!interactive || await input.ReadLineAsync() != Confirmation)
                return report with { ErrorCategory = "LIVE_CONSENT_REQUIRED", FailureStage = "PREFLIGHT" };
        }

        // Verify local validation prerequisites BEFORE a potentially chargeable call.
        string bridge;
        try { bridge = ValidationBridge.Find(); await ValidationBridge.Preflight(bridge); }
        catch { return report with { ErrorCategory = "LOCAL_VALIDATOR_UNAVAILABLE", FailureStage = "PREFLIGHT" }; }

        using var observed = new ObservedTransport(transport ?? (options.Live
            ? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }
            : new OfflineTransport()), configured.ModelIdentity);
        using var http = new HttpClient(observed) { Timeout = Timeout.InfiniteTimeSpan };
        var extractor = new GeminiDiscoveryTextExtractor(http, settings);
        using var timeout = new CancellationTokenSource(timeoutMs);
        var started = Stopwatch.GetTimestamp();
        DiscoveryTextGeneration? generation = null;
        DiscoveryTextExtractionException? failure = null;
        string? localError = null;
        try { generation = await extractor.ExtractAsync(Source, timeout.Token); }
        catch (DiscoveryTextExtractionException e) { failure = e; }
        catch { localError = "LOCAL_EXTRACTION_FAILURE"; } // Never expose arbitrary exception strings.
        report = report with {
            GenerateContentAttempts = observed.Calls, ProviderRequests = options.Live ? observed.Calls : 0,
            MockRequests = options.Live ? 0 : observed.Calls, HttpStatus = observed.Status,
            ProviderResponseReceived = observed.Status is not null, RequestDurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            FinishReason = observed.FinishReason, ResponseModelIdentity = observed.ResponseModelIdentity,
            InputTokens = observed.InputTokens, OutputTokens = observed.OutputTokens, ProviderErrorHint = observed.ErrorHint,
            Transport = observed.Status is >= 200 and < 300 ? "TRANSPORT_PASS" : "TRANSPORT_FAIL"
        };
        if (generation is null)
            return report with { ErrorCategory = failure?.Category.ToString() ?? localError,
                FailureStage = failure?.Stage.ToString() ?? "EXTRACTION", DiagnosticId = failure is null ? "GEMINI_JD_LOCAL" : $"GEMINI_JD_{failure.Category}", EndToEnd = "END_TO_END_FAIL" };
        report = report with { InputTokens = generation.InputTokens, OutputTokens = generation.OutputTokens };
        try
        {
            var validation = await ValidationBridge.Validate(bridge, generation.Output);
            var hasVacancy = validation.Fields is { } fields && fields.TryGetProperty("vacancies", out var vacancies) && vacancies.GetArrayLength() > 0;
            var endToEnd = validation.Evidence == "EVIDENCE_PASS" && hasVacancy ? "END_TO_END_PASS"
                : validation.Evidence == "EVIDENCE_ABSTAINED" ? "END_TO_END_INCOMPLETE"
                : validation.Evidence is "EVIDENCE_PASS" or "EVIDENCE_PARTIAL" ? "PARTIAL_EXTRACTION" : "END_TO_END_FAIL";
            return report with {
                JsonParse = validation.JsonParse, Schema = validation.Schema, Evidence = validation.Evidence,
                EndToEnd = endToEnd, EvidenceDiagnostics = validation.Diagnostics, MissingFacts = validation.MissingFacts,
                Coverage = validation.Coverage, ValidatedSyntheticFields = validation.Fields,
                ErrorCategory = validation.ErrorCategory, FailureStage = validation.ErrorCategory is null ? null : "LOCAL_VALIDATION"
            };
        }
        catch { return report with { ErrorCategory = "LOCAL_VALIDATOR_FAILED", FailureStage = "LOCAL_VALIDATION", EndToEnd = "END_TO_END_FAIL" }; }
    }
}
