using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Frozen recovery contract tests. TestServer, synthetic data, no new production types.
// InMemory exercises application behavior only; relational proof is a separate suite.
public sealed class DiscoveryExecutionRecoveryRedTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData(7)]
    [InlineData(2)]
    public async Task T01_T16_Preview_reads_complete_persisted_set_without_writes(int count)
    {
        using var s = await RecoveryScenario.Create(root, count); var before = await s.Snapshot();
        var first = await s.Preview(); var again = await s.Preview();
        Assert.Equal("RECOVERABLE", first.GetProperty("status").GetString());
        Assert.Equal(count, first.GetProperty("observationCount").GetInt32());
        Assert.Equal("UNKNOWN", first.GetProperty("providerResponseCompleteness").GetString());
        Assert.Equal(first.GetProperty("previewFingerprint").GetString(), again.GetProperty("previewFingerprint").GetString());
        Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Fact] public async Task T02_Recovery_never_invokes_network_dispatch_claim_or_semantic_extraction()
    {
        using var s = await RecoveryScenario.Create(root); var before = await s.ImmutableSource();
        var p = await s.Preview(); await s.Finalize(p);
        Assert.Equal(before, await s.ImmutableSource()); s.AssertOffline();
        await s.Read(async db => { Assert.Single(await db.DiscoveryExecutions.ToListAsync()); Assert.Single(await db.DiscoveryExecutionAttempts.ToListAsync()); });
    }

    [Fact] public async Task T03_T09_Finalize_is_partial_with_seven_candidates_and_no_worker_result_or_jobs()
    {
        using var s = await RecoveryScenario.Create(root); var source = await s.ImmutableSource();
        var preview = await s.Preview(); var receipt = await s.Finalize(preview);
        Assert.Equal(s.Id, receipt.GetProperty("executionId").GetGuid());
        await s.Read(async db => {
            var e = await db.DiscoveryExecutions.SingleAsync();
            Assert.Equal("PARTIAL", e.State); Assert.Equal("RECOVERED_FOR_HUMAN_REVIEW", e.ErrorCode);
            Assert.Null(e.ResultHash); Assert.Null(e.ActiveKey); Assert.Equal(s.Version + 1, e.Version);
            Assert.Equal(7, await db.DiscoveryCandidates.CountAsync()); Assert.Equal(7, await db.DiscoveryExecutionObservations.CountAsync());
            Assert.Empty(await db.JobPostings.ToListAsync()); Assert.Empty(await db.DiscoveryExecutionVacancies.ToListAsync());
            Assert.Contains(await db.DiscoveryExecutionEvents.ToListAsync(), x => x.State == "RECOVERED_FOR_HUMAN_REVIEW");
        });
        Assert.Equal(source, await s.ImmutableSource()); s.AssertOffline();
    }

    [Fact] public async Task T04_Exact_operation_replay_returns_persisted_receipt_without_any_writes()
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var op = Guid.NewGuid();
        var first = await s.Finalize(p, op); var snapshot = await s.Snapshot(); var saves = s.Fault.Saves;
        var replay = await s.Finalize(p, op);
        Assert.Equal(first.GetRawText(), replay.GetRawText()); Assert.Equal(snapshot, await s.Snapshot()); Assert.Equal(saves, s.Fault.Saves);
        Assert.Equal(op, replay.GetProperty("operationId").GetGuid()); s.AssertOffline();
    }

    [Theory]
    [InlineData("different-operation")]
    [InlineData("changed-reason")]
    [InlineData("changed-actor")]
    [InlineData("changed-execution")]
    public async Task T04_Conflicting_operation_or_changed_request_cannot_recover_again(string change)
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var op = Guid.NewGuid();
        await s.Finalize(p, op); var before = await s.Snapshot(); var request = s.Request(p, op);
        if (change == "different-operation") request["operationId"] = Guid.NewGuid();
        if (change == "changed-reason") request["reason"] = "A different synthetic reason";
        using var other = s.Admin(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        using var response = await (change == "changed-actor" ? other : s.Client).PostAsJsonAsync(
            s.FinalizePath(change == "changed-execution" ? Guid.NewGuid() : s.Id), request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Theory]
    [InlineData("operationId")]
    [InlineData("expectedVersion")]
    [InlineData("expectedWorkflowRunId")]
    [InlineData("previewFingerprint")]
    [InlineData("confirmation")]
    [InlineData("reason")]
    public async Task T04_Finalize_requires_all_confirmation_fields(string missing)
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var request = s.Request(p); request.Remove(missing);
        var before = await s.Snapshot(); using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Theory]
    [InlineData("expectedVersion")]
    [InlineData("expectedWorkflowRunId")]
    [InlineData("previewFingerprint")]
    public async Task T05_Stale_or_mismatched_confirmation_conflicts(string field)
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var request = s.Request(p);
        request[field] = field == "expectedVersion" ? JsonValue.Create(-1) : JsonValue.Create(field == "expectedWorkflowRunId" ? "10002" : new string('0', 64));
        var before = await s.Snapshot(); using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Theory]
    [InlineData("missing-cost")]
    [InlineData("unknown-cost")]
    [InlineData("contradictory-cost")]
    [InlineData("missing-evidence")]
    [InlineData("duplicate-evidence")]
    [InlineData("wrong-run")]
    [InlineData("failed-operation")]
    [InlineData("unknown-operation")]
    [InlineData("semantic-claimed")]
    [InlineData("semantic-unknown")]
    public async Task T06_T10_Unknown_or_inconsistent_provenance_blocks_the_entire_operation(string defect)
    {
        using var s = await RecoveryScenario.Create(root); await s.MutateEvidence(defect); await s.AssertBlocked();
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("hash")]
    [InlineData("identity-hash")]
    [InlineData("provider-id")]
    [InlineData("source-url")]
    [InlineData("provider-url")]
    [InlineData("raw-fk")]
    [InlineData("normalized-hash")]
    public async Task T07_Corrupted_persisted_source_is_not_repaired(string defect)
    {
        using var s = await RecoveryScenario.Create(root); await s.MutateSource(defect); await s.AssertBlocked();
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("worker")]
    [InlineData("import")]
    public async Task T08_Recovery_routes_require_Admin_not_machine_credentials(string credential)
    {
        using var s = await RecoveryScenario.Create(root); using var client = s.Factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        if (credential == "worker") { client.DefaultRequestHeaders.Add("X-Discovery-Worker-Key", RecoveryScenario.WorkerKey); client.DefaultRequestHeaders.Add("X-GitHub-Run-Id", "10001"); client.DefaultRequestHeaders.Add("X-GitHub-Run-Attempt", "1"); }
        if (credential == "import") client.DefaultRequestHeaders.Add("X-Discovery-Key", RecoveryScenario.WorkerKey);
        var before = await s.Snapshot(); using var preview = await client.GetAsync(s.PreviewPath);
        Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);
        using var final = await client.PostAsJsonAsync(s.FinalizePath(), new { operationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, final.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Fact] public async Task T10_Recovery_is_not_an_original_query_classifier_or_bundle()
    {
        using var s = await RecoveryScenario.Create(root); await s.Finalize(await s.Preview());
        await s.Read(async db => { foreach (var c in await db.DiscoveryCandidates.ToListAsync()) {
            Assert.Equal("", c.Query); Assert.Equal("NEEDS_REVIEW", c.ContentStatus); Assert.Equal("INSUFFICIENT_DATA", c.TargetStatus);
            var p = JsonNode.Parse(c.Payload)!.AsObject(); Assert.Equal("recovery-review-v1", p["version"]!.GetValue<string>());
            Assert.Null(p["originalQuery"]); Assert.Null(p["originalClassifierResult"]); Assert.Null(p["originalReviewBundle"]);
            Assert.False(p.ContainsKey("classifierVersion")); Assert.False(p.ContainsKey("evaluatedAt"));
        }}); s.AssertOffline();
    }

    [Theory]
    [InlineData("source-url")]
    [InlineData("provider-id")]
    [InlineData("raw")]
    [InlineData("bundle-version")]
    public async Task T11_Control_normal_ingestion_still_rejects_substituted_review_source(string substitution)
    {
        using var s = await RecoveryScenario.Create(root, 1); var result = await s.NormalResult();
        var item = result.ReviewBundle!.Items[0];
        item = substitution switch { "source-url" => item with { SourceUrl = "https://www.facebook.com/fixture/posts/9999" },
            "provider-id" => item with { ProviderPostId = "9999" }, "raw" => item with { RawContent = "A substituted source" }, _ => item };
        result = result with { ReviewBundle = result.ReviewBundle with { SchemaVersion = substitution == "bundle-version" ? 99 : 1, Items = [item] } };
        using var scope = s.Factory.Services.CreateScope(); s.GitHub.AllowSeedVerification = true;
        await Assert.ThrowsAsync<Portfolio.Application.Common.Exceptions.ValidationException>(() => scope.ServiceProvider.GetRequiredService<IDiscoveryExecutionService>().CompleteAsync(s.Id, "10001", result, default));
        await s.Read(async db => Assert.Empty(await db.DiscoveryCandidates.ToListAsync()));
    }

    [Fact] public async Task T11_Control_valid_normal_bundle_still_completes()
    {
        using var s = await RecoveryScenario.Create(root, 1); var result = await s.NormalResult();
        using var scope = s.Factory.Services.CreateScope(); s.GitHub.AllowSeedVerification = true;
        await scope.ServiceProvider.GetRequiredService<IDiscoveryExecutionService>().CompleteAsync(s.Id, "10001", result, default);
        await s.Read(async db => Assert.Single(await db.DiscoveryCandidates.ToListAsync()));
    }

    [Fact] public async Task T12_Application_failure_does_not_release_active_key_or_commit_candidates_audit_receipt()
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var before = await s.Snapshot(); s.Fault.FailRecoverySave = true;
        using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(p));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); Assert.True(s.Fault.RecoveryWriteObserved);
        Assert.Equal(before, await s.Snapshot()); s.AssertOffline(); // NOT relational proof.
    }

    [Fact] public async Task T12_Concurrent_different_operations_have_one_winner_application_control_only()
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(p))));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await s.Read(async db => Assert.Equal(7, await db.DiscoveryCandidates.CountAsync())); s.AssertOffline();
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("missing-text")]
    [InlineData("empty-text")]
    [InlineData("invalid-json")]
    [InlineData("unsupported-id")]
    [InlineData("unsafe-url")]
    public async Task T13_Unrepresentable_row_blocks_all_seven_without_omission(string defect)
    {
        using var s = await RecoveryScenario.Create(root, 7, initialDefect: defect); await s.AssertBlocked();
        await s.Read(async db => Assert.Equal(7, await db.DiscoveryExecutionObservations.CountAsync()));
    }

    [Fact] public async Task T13_Exactly_24000_characters_is_a_positive_boundary_not_truncated()
    {
        using var s = await RecoveryScenario.Create(root, 1, initialDefect: "boundary"); await s.Finalize(await s.Preview());
        await s.Read(async db => Assert.Contains(new string('A', 24000), (await db.DiscoveryCandidates.SingleAsync()).Payload));
    }

    [Theory]
    [InlineData("execution-version")]
    [InlineData("configuration")]
    [InlineData("attempt")]
    [InlineData("provider-evidence")]
    [InlineData("observation")]
    [InlineData("raw-metadata")]
    [InlineData("semantic")]
    [InlineData("candidate-review")]
    [InlineData("event")]
    public async Task T14_Recovery_dependency_change_invalidates_saved_preview(string dependency)
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); await s.MutateDependency(dependency);
        var before = await s.Snapshot(); using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(p));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }

    [Fact] public async Task T15_Existing_reviewed_classifier_candidate_is_not_overwritten_or_relabelled()
    {
        using var s = await RecoveryScenario.Create(root); await s.AddNormalCandidate(reviewed: true, sameRun: true); await s.AssertBlocked();
    }

    [Fact] public async Task T17_Detail_projection_has_typed_recovery_and_no_synthetic_Item()
    {
        using var s = await RecoveryScenario.Create(root); await s.Finalize(await s.Preview()); var c = await s.FirstCandidate();
        var dto = await RecoveryScenario.Data(await s.Client.GetAsync($"{RecoveryScenario.ReviewPath}/{c.Id}"));
        Assert.Equal("PERSISTED_OBSERVATION_RECOVERY", dto.GetProperty("origin").GetString());
        Assert.Equal("", dto.GetProperty("query").GetString()); Assert.Equal(JsonValueKind.Null, dto.GetProperty("originalQuery").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("item").ValueKind); Assert.Equal(JsonValueKind.Object, dto.GetProperty("recovery").ValueKind);
        Assert.Equal("UNREVIEWED", dto.GetProperty("humanReview").GetProperty("status").GetString());
        Assert.Equal(0, dto.GetProperty("humanReview").GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("REVIEW_RECOVERY", "REVIEWED_RECOVERY")]
    [InlineData("UNCERTAIN", "UNCERTAIN")]
    public async Task T17_Recovery_human_label_has_no_original_classifier_to_confirm(string action, string expected)
    {
        using var s = await RecoveryScenario.Create(root); var p = await s.Preview(); var op = Guid.NewGuid(); await s.Finalize(p, op); var c = await s.FirstCandidate();
        var body = new { expectedVersion = 0, action, contentStatus = action == "UNCERTAIN" ? null : "VALID_RECRUITMENT", targetStatus = action == "UNCERTAIN" ? null : "NOT_MATCHED", notes = "Synthetic human evaluation" };
        var saved = await RecoveryScenario.Data(await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/{c.Id}/reviews", body));
        Assert.Contains(expected, saved.GetRawText()); var beforeReplay = await s.Snapshot(); await s.Finalize(p, op); Assert.Equal(beforeReplay, await s.Snapshot());
        var history = await RecoveryScenario.Data(await s.Client.GetAsync($"{RecoveryScenario.ReviewPath}/{c.Id}/reviews")); Assert.Contains("Synthetic human evaluation", history.GetRawText());
        using var stale = await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/{c.Id}/reviews", body); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("CORRECTED")]
    public async Task T17_Recovery_rejects_original_classifier_actions(string action)
    {
        using var s = await RecoveryScenario.Create(root); await s.Finalize(await s.Preview()); var c = await s.FirstCandidate();
        using var response = await s.Client.PostAsJsonAsync($"{RecoveryScenario.ReviewPath}/{c.Id}/reviews", new { expectedVersion = 0, action, notes = "No original classifier" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] public async Task T17_Recovery_does_not_change_normal_prediction_matrices_even_for_duplicate_content()
    {
        using var s = await RecoveryScenario.Create(root); await s.AddNormalCandidate(reviewed: true);
        var baseline = await RecoveryScenario.Data(await s.Client.GetAsync($"{RecoveryScenario.ReviewPath}/quality-metrics"));
        await s.Finalize(await s.Preview());
        var mixed = await RecoveryScenario.Data(await s.Client.GetAsync($"{RecoveryScenario.ReviewPath}/quality-metrics"));
        var normal = mixed.GetProperty("cohorts").EnumerateArray().Single(x => x.GetProperty("classifierVersion").GetString() == "content-first-v1");
        Assert.Equal(baseline.GetProperty("cohorts")[0].GetProperty("observationLevel").GetRawText(), normal.GetProperty("observationLevel").GetRawText());
        Assert.Equal(baseline.GetProperty("cohorts")[0].GetProperty("uniqueContentLevel").GetRawText(), normal.GetProperty("uniqueContentLevel").GetRawText());
        var recovery = mixed.GetProperty("recoveryCoverage"); Assert.Equal(7, recovery.GetProperty("total").GetInt32());
        Assert.True(recovery.GetProperty("predictionUnavailable").GetBoolean()); Assert.Equal(JsonValueKind.Null, recovery.GetProperty("precision").ValueKind);
    }

    [Fact] public async Task T18_Intact_numeric_PFBID_mismatch_is_reviewable_but_never_promoted()
    {
        using var s = await RecoveryScenario.Create(root, mismatch: true); var original = await s.ImmutableSource();
        await s.Finalize(await s.Preview()); var c = await s.FirstCandidate(); Assert.True(c.IdentityWarning);
        Assert.Contains("POST_ID_URL_MISMATCH", c.Payload); Assert.Contains("pfbidSyntheticMismatch", c.Payload);
        Assert.Equal(original, await s.ImmutableSource()); await s.Read(async db => Assert.Empty(await db.JobPostings.ToListAsync())); s.AssertOffline();
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("raw")]
    [InlineData("provider-id")]
    [InlineData("provider-url")]
    [InlineData("source-url")]
    [InlineData("outcome")]
    [InlineData("raw-metadata")]
    public async Task T18_Identity_warning_never_excuses_real_corruption(string defect)
    {
        using var s = await RecoveryScenario.Create(root, mismatch: true); await s.MutateSource(defect); await s.AssertBlocked();
    }
}

internal sealed class RecoveryScenario : IDisposable
{
    internal const string ReviewPath = "/api/v1/admin/job-hunting/discovery";
    internal const string WorkerKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal readonly Guid Id = Guid.NewGuid();
    internal readonly RecoveryGitHub GitHub = new(); internal readonly RecoveryNetwork Network = new();
    internal readonly RecoverySaveFault Fault = new(); internal WebApplicationFactory<Program> Factory = null!;
    internal HttpClient Client = null!; internal int Version;
    internal string PreviewPath => $"{ReviewPath}/executions/{Id}/recovery";
    internal string FinalizePath(Guid? id = null) => $"{ReviewPath}/executions/{id ?? Id}/recovery/finalize";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<RecoveryScenario> Create(AuthApiFactory root, int count = 7, bool mismatch = false, string? initialDefect = null,
        Action<IServiceCollection, RecoverySaveFault>? database = null)
    {
        var s = new RecoveryScenario(); var name = "recovery_" + Guid.NewGuid().ToString("N");
        s.Factory = root.WithWebHostBuilder(b => {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["DiscoveryExecution:Enabled"] = "true", ["DiscoveryExecution:WorkerEnabled"] = "true", ["DiscoveryExecution:WorkerKey"] = WorkerKey,
                ["DiscoveryExecution:FreePlanConfirmed"] = "true", ["DiscoveryExecution:NoCashConfirmed"] = "true" }));
            b.ConfigureServices(services => {
                services.RemoveAll<IApplicationDbContext>();
                if (database is null) services.AddScoped<IApplicationDbContext>(_ => new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name).AddInterceptors(s.Fault).Options));
                else database(services, s.Fault);
                services.RemoveAll<IDiscoveryGitHubClient>(); services.AddSingleton<IDiscoveryGitHubClient>(s.GitHub);
                services.RemoveAll<IDiscoveryTextExtractor>(); services.AddSingleton<IDiscoveryTextExtractor>(new RecoverySemantic(s.Network));
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(s.Network);
            });
        });
        s.Client = s.Admin();
        await s.Read(async db => {
            var now = new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero);
            db.DiscoveryExecutions.Add(new() { Id = s.Id, ClientRequestId = Guid.NewGuid(), CreatedByAdminId = AuthApiFactory.AdminId,
                State = "CLAIMED", WorkflowRunId = "10001", Version = 3, CreatedAt = now, UpdatedAt = now });
            db.DiscoveryExecutionAttempts.Add(new() { Id = s.Id, ExecutionId = s.Id, WorkflowRunId = "10001", ClaimedAt = now });
            await db.SaveChangesAsync();
        });
        var observations = Enumerable.Range(0, count).Select(index => {
            string? raw = $"Synthetic employer {index} hiring Backend Fresher in Hanoi. C# required.";
            if (index == 0 && initialDefect is "boundary" or "oversized") raw = new string('A', initialDefect == "boundary" ? 24000 : 24001);
            if (index == 0 && initialDefect == "missing-text") raw = null;
            if (index == 0 && initialDefect == "empty-text") raw = "";
            var id = index == 0 && initialDefect == "unsupported-id" ? "unsupported:synthetic" : (9001 + index).ToString(); var bad = mismatch && index == 0;
            string? url = bad ? null : $"https://www.facebook.com/fixture/posts/{id}";
            if (index == 0 && initialDefect == "unsafe-url") url = "https://example.invalid/unsafe";
            return JsonSerializer.SerializeToElement(new { itemIndex = index, providerPostId = id, sourceUrl = url,
                providerSourceUrl = bad ? "https://www.facebook.com/fixture/posts/pfbidSyntheticMismatch" : url,
                rawContent = raw, contentHash = raw is null ? null : DiscoveryReviewRules.Hash(raw), publishedAt = (string?)null,
                outcome = bad ? "POST_ID_URL_MISMATCH" : "READY_FOR_EXTRACTION" }, Json);
        }).ToArray();
        using (var scope = s.Factory.Services.CreateScope()) {
            s.GitHub.AllowSeedVerification = true;
            await scope.ServiceProvider.GetRequiredService<IDiscoveryExecutionService>().ObservationsAsync(s.Id, "10001", Result(observations), default);
            s.GitHub.AllowSeedVerification = false;
        }
        await s.Read(async db => s.Version = (await db.DiscoveryExecutions.SingleAsync()).Version);
        if (initialDefect == "invalid-json") await s.Read(async db => { (await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync()).PayloadJson = "{"; await db.SaveChangesAsync(); });
        return s;
    }

    internal static DiscoveryExecutionResult Result(JsonElement[] observations) => new(1, 1, 6, 200, observations,
        ProviderOperation: "SUCCEEDED", UsageEvidence: new(false, JsonSerializer.SerializeToElement<object?>(null), "1", "6"));
    internal HttpClient Admin(Guid? actor = null) {
        var client = Factory.CreateClient(new() { BaseAddress = new("https://localhost") }); using var scope = Factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(actor ?? AuthApiFactory.AdminId, "synthetic@example.invalid");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value); return client;
    }
    internal async Task Read(Func<IApplicationDbContext, Task> use) { using var scope = Factory.Services.CreateScope(); await use(scope.ServiceProvider.GetRequiredService<IApplicationDbContext>()); }
    internal async Task<JsonElement> Preview() => await Data(await Client.GetAsync(PreviewPath));
    internal JsonObject Request(JsonElement preview, Guid? op = null) => new() {
        ["operationId"] = (op ?? Guid.NewGuid()).ToString(), ["expectedVersion"] = Version, ["expectedWorkflowRunId"] = "10001",
        ["previewFingerprint"] = preview.GetProperty("previewFingerprint").GetString(), ["confirmation"] = "RECOVER PERSISTED OBSERVATIONS", ["reason"] = "Recover synthetic persisted observations" };
    internal async Task<JsonElement> Finalize(JsonElement preview, Guid? op = null) => await Data(await Client.PostAsJsonAsync(FinalizePath(), Request(preview, op)));
    internal static async Task<JsonElement> Data(HttpResponseMessage response) {
        using (response) { Assert.True(response.IsSuccessStatusCode, $"Missing required recovery behavior: HTTP {(int)response.StatusCode} {response.StatusCode}");
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone(); }
    }
    internal void AssertOffline() { Assert.Equal(0, GitHub.UnexpectedCalls); Assert.Empty(Network.Calls); }
    internal async Task AssertBlocked() {
        var before = await Snapshot(); var p = await Preview(); Assert.Contains(p.GetProperty("status").GetString(), new[] { "UNKNOWN", "INCOMPLETE" });
        Assert.NotEmpty(p.GetProperty("blockers").EnumerateArray()); using var response = await Client.PostAsJsonAsync(FinalizePath(), Request(p));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await Snapshot()); AssertOffline();
    }
    internal async Task<string> ImmutableSource() { string value = ""; await Read(async db => value = JsonSerializer.Serialize(new {
        attempts = await db.DiscoveryExecutionAttempts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        observations = await db.DiscoveryExecutionObservations.AsNoTracking().OrderBy(x => x.ItemIndex).ToArrayAsync(),
        raw = await db.RawJobPostings.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        evidence = await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.State == "PROVIDER_EVIDENCE").OrderBy(x => x.Id).ToArrayAsync() }, Json)); return value; }
    internal async Task<string> Snapshot() { string value = ""; var source = await ImmutableSource(); await Read(async db => value = JsonSerializer.Serialize(new { source,
        executions = await db.DiscoveryExecutions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), candidates = await db.DiscoveryCandidates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        events = await db.DiscoveryExecutionEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), reviews = await db.DiscoveryCandidateReviews.AsNoTracking().OrderBy(x => x.CandidateId).ToArrayAsync(),
        reviewEvents = await db.DiscoveryCandidateReviewEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), jobs = await db.JobPostings.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() }, Json)); return value; }
    internal async Task<DiscoveryCandidate> FirstCandidate() { DiscoveryCandidate value = null!; await Read(async db => value = await db.DiscoveryCandidates.AsNoTracking().Where(x => x.RunId == Id.ToString()).OrderBy(x => x.ItemIndex).FirstAsync()); return value; }

    internal Task MutateEvidence(string defect) => Read(async db => {
        var a = await db.DiscoveryExecutionAttempts.SingleAsync(); var e = await db.DiscoveryExecutionEvents.SingleAsync(x => x.State == "PROVIDER_EVIDENCE");
        if (defect == "missing-cost") a.CreditsDeducted = null;
        if (defect == "unknown-cost") a.ActualRequests = null;
        if (defect == "wrong-run") a.WorkflowRunId = "10002";
        if (defect == "missing-evidence") db.DiscoveryExecutionEvents.Remove(e);
        if (defect == "duplicate-evidence") db.DiscoveryExecutionEvents.Add(new() { Id = Guid.NewGuid(), ExecutionId = Id, State = e.State, Reason = e.Reason, At = e.At });
        if (defect is "contradictory-cost" or "failed-operation" or "unknown-operation") { var json = JsonNode.Parse(e.Reason!)!;
            if (defect == "contradictory-cost") json["usageEvidence"]!["deductedHeader"] = "2";
            else json["providerOperation"] = defect == "failed-operation" ? "FAILED" : "UNKNOWN"; e.Reason = json.ToJsonString(); }
        if (defect.StartsWith("semantic-")) (await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync()).SemanticState = defect == "semantic-claimed" ? "CLAIMED" : "UNKNOWN";
        await db.SaveChangesAsync();
    });
    internal Task MutateSource(string defect) => Read(async db => {
        var o = await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync(); var json = JsonNode.Parse(o.PayloadJson)!;
        if (defect == "raw") json["rawContent"] = "Tampered synthetic source";
        if (defect == "hash") o.ContentHash = new string('0', 64);
        if (defect == "identity-hash") o.PayloadHash = new string('0', 64);
        if (defect == "provider-id") json["providerPostId"] = "unsupported:substituted";
        if (defect == "source-url") json["sourceUrl"] = "https://example.invalid/unsafe";
        if (defect == "provider-url") json["providerSourceUrl"] = "https://www.facebook.com/fixture/posts/88888";
        if (defect == "outcome") json["outcome"] = "READY_FOR_EXTRACTION";
        if (defect == "raw-fk") o.RawJobPostingId = Guid.NewGuid();
        if (defect == "normalized-hash") (await db.RawJobPostings.OrderBy(x => x.SourceExternalId).FirstAsync()).ContentHash = new string('0', 64);
        if (defect == "raw-metadata") (await db.RawJobPostings.OrderBy(x => x.SourceExternalId).FirstAsync()).Metadata = JsonDocument.Parse("{\"providerPostId\":\"88888\"}");
        o.PayloadJson = json.ToJsonString(); await db.SaveChangesAsync();
    });
    internal Task MutateDependency(string field) => Read(async db => {
        var e = await db.DiscoveryExecutions.SingleAsync();
        if (field == "execution-version") e.Version++;
        if (field == "configuration") e.ConfigurationJson = "{\"changed\":true}";
        if (field == "attempt") (await db.DiscoveryExecutionAttempts.SingleAsync()).CreditsUsedAlternateHeader = 7;
        if (field == "provider-evidence") (await db.DiscoveryExecutionEvents.SingleAsync(x => x.ExecutionId == Id && x.State == "PROVIDER_EVIDENCE")).At += TimeSpan.FromSeconds(1);
        if (field == "observation") (await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync()).ItemIndex = 99;
        if (field == "raw-metadata") (await db.RawJobPostings.OrderBy(x => x.SourceExternalId).FirstAsync()).Metadata = JsonDocument.Parse("{\"changed\":true}");
        if (field == "semantic") (await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync()).SemanticState = "CLAIMED";
        if (field == "candidate-review") db.DiscoveryCandidates.Add(new() { Id = Guid.NewGuid(), RunId = Id.ToString(), Query = "Synthetic original query", PageNumber = 1, ItemIndex = 0, Payload = "{}", PayloadHash = new string('a', 64), ContentStatus = "NEEDS_REVIEW", TargetStatus = "INSUFFICIENT_DATA" });
        if (field == "event") db.DiscoveryExecutionEvents.Add(new() { Id = Guid.NewGuid(), ExecutionId = Id, State = "SYNTHETIC_AUDIT", At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    });
    internal async Task<DiscoveryExecutionResult> NormalResult() { DiscoveryExecutionResult value = null!; await Read(async db => {
        var o = await db.DiscoveryExecutionObservations.OrderBy(x => x.ItemIndex).FirstAsync(); var source = JsonDocument.Parse(o.PayloadJson).RootElement.Clone();
        var raw = source.GetProperty("rawContent").GetString()!; var hash = DiscoveryReviewRules.Hash(raw);
        var item = new DiscoveryReviewItem(0, source.GetProperty("providerPostId").GetString(), source.GetProperty("sourceUrl").GetString(), raw, hash,
            DiscoveryReviewRules.Hash(raw.Normalize().Replace("\r\n", "\n").Replace('\r', '\n').Trim()), null,
            new("NEEDS_REVIEW", ["SYNTHETIC_REVIEW"], "Synthetic explanation", []), new("INSUFFICIENT_DATA", ["SYNTHETIC_REVIEW"], "Synthetic explanation", []),
            "NOT_CHECKED", JsonSerializer.SerializeToElement(new { }), [], [], "content-first-v1", DateTimeOffset.UtcNow);
        value = Result([source]) with { ReviewBundle = new(1, "SOCIALAPIS", Id.ToString(), "Synthetic original query", 1, [item]) };
    }); return value; }
    internal async Task AddNormalCandidate(bool reviewed, bool sameRun = false) {
        var result = await NormalResult(); var item = result.ReviewBundle!.Items[0];
        var payload = JsonSerializer.Serialize(item, Json); var query = "Synthetic original query";
        await Read(async db => { var c = new DiscoveryCandidate { Id = Guid.NewGuid(), RunId = sameRun ? Id.ToString() : "synthetic-original-run", Query = query,
            PageNumber = 1, ItemIndex = 0, ContentStatus = item.ContentValidity.Status, TargetStatus = item.TargetMatching.Status, ContentGroupHash = item.ContentGroupHash,
            Payload = payload, PayloadHash = DiscoveryReviewRules.Hash(query + "\n" + payload), IngestedAt = DateTimeOffset.UtcNow };
            db.DiscoveryCandidates.Add(c); if (reviewed) db.DiscoveryCandidateReviews.Add(new() { CandidateId = c.Id, Version = 1, Status = "CORRECTED",
                ContentStatus = "VALID_RECRUITMENT", TargetStatus = "NOT_MATCHED", Notes = "Preserve synthetic human decision", ReviewedAt = DateTimeOffset.UtcNow, ReviewedByAdminId = AuthApiFactory.AdminId }); await db.SaveChangesAsync(); });
    }
    public void Dispose() { try { AssertOffline(); } finally { Client?.Dispose(); Factory?.Dispose(); } }
}

internal sealed class RecoveryGitHub : IDiscoveryGitHubClient
{
    internal bool AllowSeedVerification; internal int UnexpectedCalls;
    public Task<string> DispatchAsync(Guid id, CancellationToken ct) { Interlocked.Increment(ref UnexpectedCalls); throw new InvalidOperationException("TEST_DISPATCH_FORBIDDEN"); }
    public Task<bool> VerifyRunAsync(Guid id, string runId, CancellationToken ct) {
        if (AllowSeedVerification) return Task.FromResult(runId == "10001");
        Interlocked.Increment(ref UnexpectedCalls); throw new InvalidOperationException("TEST_GITHUB_FORBIDDEN");
    }
}
internal sealed class RecoveryNetwork : IHttpMessageHandlerBuilderFilter
{
    internal readonly ConcurrentQueue<string> Calls = new();
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => b => { next(b); b.PrimaryHandler = new Denied(this); };
    private sealed class Denied(RecoveryNetwork owner) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { owner.Calls.Enqueue("HTTP"); throw new InvalidOperationException("TEST_NETWORK_FORBIDDEN"); }
    }
}
internal sealed class RecoverySemantic(RecoveryNetwork network) : IDiscoveryTextExtractor
{
    public string ModelIdentity => "SYNTHETIC_NO_NETWORK";
    public Task<DiscoveryTextGeneration> ExtractAsync(string rawContent, CancellationToken ct) { network.Calls.Enqueue("SEMANTIC"); throw new InvalidOperationException("TEST_SEMANTIC_FORBIDDEN"); }
}
internal sealed class RecoverySaveFault : SaveChangesInterceptor
{
    internal bool FailRecoverySave, RecoveryWriteObserved; internal int Saves;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) {
        Interlocked.Increment(ref Saves);
        if (FailRecoverySave && data.Context!.ChangeTracker.Entries<DiscoveryExecution>().Any(x => x.Entity.State == "PARTIAL")) {
            RecoveryWriteObserved = true; throw new InvalidOperationException("SYNTHETIC_RECOVERY_SAVE_FAILURE");
        }
        return ValueTask.FromResult(result);
    }
}
