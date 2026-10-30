using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.Infrastructure.Persistence;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Test-only wire contracts; no references to unimplemented production types.
// TestServer + EF InMemory; ALL typed/named HttpClients intercepted below.
public sealed class DiscoveryExecutionRedTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private const string AdminPath = "/api/v1/admin/job-hunting/discovery/executions";
    private const string WorkerPath = "/api/v1/discovery-execution-worker";
    private const string WorkerKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("POST", AdminPath)]
    [InlineData("GET", AdminPath)]
    [InlineData("GET", AdminPath + "/11111111-1111-1111-1111-111111111111")]
    public async Task T01_Execution_routes_require_Admin(string method, string path)
    {
        using var s = Setup(); using var client = s.Factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = method == "POST" ? JsonContent.Create(new { clientRequestId = Guid.NewGuid() }) : null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(s.Transport.Dispatches);
    }

    [Theory]
    [InlineData("query", "arbitrary provider query")]
    [InlineData("token", "SYNTHETIC_NOT_A_REAL_SECRET")]
    [InlineData("workflowPolicy", "{}")]
    [InlineData("maxRequests", 6)]
    [InlineData("maxCredits", 6)]
    public async Task T01_Browser_cannot_override_server_inputs(string field, object value)
    {
        using var s = Setup(); using var admin = Admin(s);
        var body = new Dictionary<string, object> { ["clientRequestId"] = Guid.NewGuid(), [field] = value };
        var response = await admin.PostAsJsonAsync(AdminPath, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(s.Transport.Dispatches);
    }

    [Fact] public async Task T02_Repeated_click_returns_same_execution_and_dispatches_once()
    {
        using var s = Setup(); using var admin = Admin(s); var key = Guid.NewGuid();
        var first = await Start(admin, key); var again = await Start(admin, key);
        Assert.Equal(first, again); Assert.Single(s.Transport.Dispatches);
        var status = await Data(await admin.GetAsync($"{AdminPath}/{first}"));
        Assert.Equal(0, status.GetProperty("actualRequests").GetInt32());
        Assert.Equal("ADMIN_MANUAL", status.GetProperty("triggerSource").GetString());
    }

    [Fact] public async Task T02_Different_concurrent_clicks_cannot_dispatch_overlapping_executions()
    {
        using var s = Setup(); using var admin = Admin(s);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync(AdminPath, new { clientRequestId = Guid.NewGuid() })));
        Assert.Contains(responses, r => r.IsSuccessStatusCode);
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(s.Transport.Dispatches);
        // InMemory is only a handler control, NOT proof of PostgreSQL uniqueness.
    }

    [Fact] public async Task T03_Backend_dispatches_only_bounded_execution_id_not_user_policy_or_secrets()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin);
        var payload = JsonDocument.Parse(Assert.Single(s.Transport.Dispatches)).RootElement;
        Assert.Equal("main", payload.GetProperty("ref").GetString());
        var inputs = payload.GetProperty("inputs");
        Assert.Single(inputs.EnumerateObject()); Assert.Equal(id, inputs.GetProperty("execution_id").GetGuid());
        Assert.DoesNotContain("portfolioSnapshot", payload.GetRawText());
        Assert.DoesNotContain(WorkerKey, payload.GetRawText());
    }

    [Fact] public async Task T03_Worker_config_uses_frozen_Portfolio_not_browser_supplied_skills()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        var config = await Data(await worker.GetAsync($"{WorkerPath}/{id}/config"));
        Assert.Equal(1, config.GetProperty("maxRequests").GetInt32()); Assert.Equal(1, config.GetProperty("maxCredits").GetInt32());
        Assert.Contains("C#", config.GetProperty("portfolioSnapshot").GetRawText());
        Assert.DoesNotContain("checkpoint", config.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", config.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task T04_Dispatch_timeout_is_durable_unknown_and_never_redispatched()
    {
        using var s = Setup(); s.Transport.DispatchTimeout = true; using var admin = Admin(s); var key = Guid.NewGuid();
        var response = await admin.PostAsJsonAsync(AdminPath, new { clientRequestId = key });
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        await admin.PostAsJsonAsync(AdminPath, new { clientRequestId = key });
        Assert.Single(s.Transport.Dispatches);
        var history = await Data(await admin.GetAsync(AdminPath));
        Assert.Contains("DISPATCH_UNKNOWN", history.GetRawText());
        using var scope = s.Factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().RawJobPostings.ToListAsync());
    }

    [Fact] public async Task T05_Only_one_worker_can_claim_the_provider_slot()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { })));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var status = await Data(await admin.GetAsync($"{AdminPath}/{id}"));
        Assert.Equal(JsonValueKind.Null, status.GetProperty("creditsDeducted").ValueKind);
        Assert.Empty(s.Transport.ProviderCalls);
    }

    [Fact] public async Task T05_Rerun_cannot_claim_again()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
        worker.DefaultRequestHeaders.Remove("X-GitHub-Run-Attempt"); worker.DefaultRequestHeaders.Add("X-GitHub-Run-Attempt", "2");
        var response = await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { });
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
        Assert.Empty(s.Transport.ProviderCalls);
    }

    [Theory]
    [InlineData("GET", "/config")]
    [InlineData("POST", "/claim")]
    [InlineData("POST", "/results")]
    public async Task T01_Worker_routes_do_not_accept_an_Admin_token_instead_of_machine_auth(string method, string suffix)
    {
        using var s = Setup(); using var admin = Admin(s);
        var response = await admin.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{WorkerPath}/{Guid.NewGuid()}{suffix}")
            { Content = method == "POST" ? JsonContent.Create(new { }) : null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Empty(s.Transport.ProviderCalls);
    }

    [Fact] public async Task T09_T14_T20_Real_ingestion_handlers_stage_JD_and_raw_link_idempotently()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
        var result = Result();
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", result));
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", result));
        var status = await Data(await admin.GetAsync($"{AdminPath}/{id}"));
        Assert.Equal(1, status.GetProperty("actualRequests").GetInt32());
        Assert.Equal(1, status.GetProperty("creditsDeducted").GetInt32());
        Assert.Equal(6, status.GetProperty("creditsUsedAlternateHeader").GetInt32());
        using var scope = s.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Empty(await db.JobPostings.ToListAsync()); var raw = Assert.Single(await db.RawJobPostings.ToListAsync());
        var vacancy=Assert.Single(await db.DiscoveryExecutionVacancies.ToListAsync());
        Assert.Equal(raw.Id,vacancy.RawJobPostingId);Assert.Null(vacancy.JobPostingId);Assert.Null(raw.JobPostingId);
        Assert.Equal("Synthetic Software",JsonDocument.Parse(vacancy.FieldsJson).RootElement.GetProperty("companyName").GetProperty("value").GetString());
        Assert.Equal("NEEDS_REVIEW",vacancy.Status);Assert.Equal("9001", raw.SourceExternalId);
        Assert.Contains("Fresher", raw.RawContent);
        Assert.Empty(await db.JobApplications.ToListAsync()); Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        Assert.True(status.GetProperty("results")[0].GetProperty("staging").GetBoolean());
        Assert.Empty(s.Transport.ProviderCalls);
    }

    [Fact] public async Task T11_Missing_company_and_location_stay_staging_not_fabricated_Jobs()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", Result(missing: true)));
        using var scope = s.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Single(await db.RawJobPostings.ToListAsync()); Assert.Empty(await db.JobPostings.ToListAsync());
        var status = await Data(await admin.GetAsync($"{AdminPath}/{id}"));
        Assert.Contains("NOT_PROVIDED", status.GetRawText());
        Assert.Contains("staging", status.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task T14_Changed_callback_payload_cannot_overwrite_the_first_result()
    {
        using var s = Setup(); using var admin = Admin(s); var id = await Start(admin); using var worker = Worker(s);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", Result()));
        var changed = await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", Result(missing: true));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var scope = s.Factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().JobPostings.ToListAsync());
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().DiscoveryExecutionVacancies.ToListAsync());
    }

    [Fact] public async Task T08_Cross_execution_reposts_keep_raw_identity_without_duplicate_Jobs()
    {
        using var s = Setup(); using var admin = Admin(s);
        foreach (var index in Enumerable.Range(0, 2)) {
            using var worker = Worker(s, (10001 + index).ToString());
            var id = await Start(admin);
            await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
            await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", Result()));
        }
        using var scope = s.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Empty(await db.JobPostings.ToListAsync());
        Assert.Equal(2,await db.DiscoveryExecutionObservations.CountAsync());
        Assert.Equal("9001", Assert.Single(await db.RawJobPostings.ToListAsync()).SourceExternalId);
    }

    [Fact] public async Task T10_Multiple_vacancies_share_one_raw_source_without_automatic_Job_creation()
    {
        using var s = Setup(); using var admin = Admin(s); using var worker = Worker(s); var id = await Start(admin);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/claim", new { }));
        var result = JsonSerializer.SerializeToNode(Result())!;
        var observation = result["observations"]![0]!;
        var raw = observation["rawContent"]!.GetValue<string>() + "\nAlso hiring Intern Python Backend Developer in Hanoi.";
        observation["rawContent"] = raw; observation["contentHash"] = Portfolio.Application.Features.JobHunting.DiscoveryReviewRules.Hash(raw);
        var vacancies = observation["vacancies"]!.AsArray(); var second = vacancies[0]!.DeepClone();
        second["positionTitle"] = "Intern Python Backend Developer"; second["technologyStack"] = new JsonArray("Python");
        vacancies.Add(second);
        await Data(await worker.PostAsJsonAsync($"{WorkerPath}/{id}/results", result));
        using var scope = s.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Empty(await db.JobPostings.ToListAsync());
        var staged=await db.DiscoveryExecutionVacancies.ToListAsync();Assert.Equal(2,staged.Count);
        Assert.Equal("9001", Assert.Single(await db.RawJobPostings.ToListAsync()).SourceExternalId);
        Assert.All(staged,v=>{Assert.NotNull(v.RawJobPostingId);Assert.Null(v.JobPostingId);});
        Assert.Single(staged.Select(v=>v.RawJobPostingId).Distinct());
    }

    [Fact] public void T18_Application_has_one_reusable_orchestration_port_not_only_a_controller()
    {
        var contract = typeof(IApplicationDbContext).Assembly.GetType("Portfolio.Application.Features.JobHunting.IDiscoveryExecutionService");
        Assert.NotNull(contract); Assert.True(contract!.IsInterface);
        var start = contract.GetMethod("StartAsync"); Assert.NotNull(start);
        Assert.Contains(start!.GetParameters(), p => p.Name == "triggerSource");
    }

    [Fact] public void T19_Production_model_has_durable_execution_and_attempt_persistence()
    {
        // Model construction only: no connection opened, schema created or migration applied.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=synthetic_never_open;Username=synthetic;Password=synthetic", options => options.UseVector()).Options);
        Assert.Contains(db.Model.GetEntityTypes(), e => e.GetTableName() == "discovery_executions");
        Assert.Contains(db.Model.GetEntityTypes(), e => e.GetTableName() == "discovery_execution_attempts");
    }

    [Fact] public async Task T16_Control_existing_Admin_Discovery_list_is_accessible_without_provider()
    {
        using var s = Setup(); using var admin = Admin(s);
        var data = await Data(await admin.GetAsync("/api/v1/admin/job-hunting/discovery"));
        Assert.Equal(0, data.GetProperty("results").GetProperty("total").GetInt32());
        Assert.Empty(s.Transport.Dispatches); Assert.Empty(s.Transport.ProviderCalls);
    }

    private static object Result(bool missing = false)
    {
        const string raw = "Synthetic Software hiring Fresher .NET Backend Developer in Hanoi. C#, ASP.NET Core. Apply jobs@example.invalid.";
        return new { actualRequests = 1, creditsDeducted = 1, creditsUsedAlternateHeader = 6, httpStatus = 200,
            observations = new[] { new { itemIndex = 0, providerPostId = "9001", sourceUrl = "https://www.facebook.com/synthetic/posts/9001",
                rawContent = raw, contentHash = Portfolio.Application.Features.JobHunting.DiscoveryReviewRules.Hash(raw), publishedAt = (string?)null,
                vacancies = new[] { new { companyName = missing ? null : "Synthetic Software", positionTitle = "Fresher .NET Backend Developer",
                    location = missing ? null : "Hanoi", description = raw, technologyStack = new[] { "C#", "ASP.NET Core" },
                    applicationEmail = "jobs@example.invalid", fieldStatuses = new { companyName = missing ? "NOT_PROVIDED" : "EXTRACTED", location = missing ? "NOT_PROVIDED" : "EXTRACTED" } } } } } };
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Expected execution behavior; HTTP {(int)response.StatusCode} {response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
    }
    private static async Task<Guid> Start(HttpClient client, Guid? key = null) =>
        (await Data(await client.PostAsJsonAsync(AdminPath, new { clientRequestId = key ?? Guid.NewGuid() }))).GetProperty("id").GetGuid();
    private static HttpClient Admin(Scenario s)
    {
        var client = s.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var scope = s.Factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId, "admin@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value); return client;
    }
    private static HttpClient Worker(Scenario s, string runId = "10001")
    {
        var client = s.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Discovery-Worker-Key", WorkerKey);
        client.DefaultRequestHeaders.Add("X-GitHub-Run-Id", runId); client.DefaultRequestHeaders.Add("X-GitHub-Run-Attempt", "1"); return client;
    }
    private Scenario Setup()
    {
        var database = Guid.NewGuid().ToString(); var transport = new OfflineTransport();
        var factory = root.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["DiscoveryExecution:Enabled"] = "true", ["DiscoveryExecution:WorkerEnabled"] = "true", ["DiscoveryExecution:WorkerKey"] = WorkerKey,
                ["DiscoveryExecution:GitHubToken"] = "SYNTHETIC_GITHUB_TOKEN", ["DiscoveryExecution:Repository"] = "CoderPL1005/portfolio",
                ["DiscoveryExecution:Workflow"] = "job-discovery-ingestion.yml", ["DiscoveryExecution:FreePlanConfirmed"] = "true", ["DiscoveryExecution:NoCashConfirmed"] = "true" }));
            b.ConfigureServices(services => {
                services.RemoveAll<IApplicationDbContext>();
                services.AddScoped<IApplicationDbContext>(_ => new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(database).Options));
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport);
            });
        });
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var now = DateTimeOffset.UtcNow; var technology = Guid.NewGuid();
        db.Technologies.Add(new Technology { Id = technology, Name = "C#", Category = "BACKEND", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Skills.Add(new Skill { Id = Guid.NewGuid(), Name = "C#", Category = "BACKEND", ExperienceLevel = "USED", TechnologyId = technology, IsPublished = true, CreatedAt = now, UpdatedAt = now });
        db.CandidateJobPreferences.Add(new CandidateJobPreferences { Id = Guid.NewGuid(), TargetRoles = JsonDocument.Parse("[\"Backend Developer\"]"),
            PreferredTechnologies = JsonDocument.Parse("[\"C#\"]"), AcceptableLocations = JsonDocument.Parse("[\"Hanoi\"]"), WorkplaceTypes = JsonDocument.Parse("[]"),
            EmploymentTypes = JsonDocument.Parse("[]"), CreatedAt = now, UpdatedAt = now });
        db.SaveChangesAsync().GetAwaiter().GetResult(); return new(factory, transport);
    }
    private sealed record Scenario(WebApplicationFactory<Program> Factory, OfflineTransport Transport) : IDisposable { public void Dispose() => Factory.Dispose(); }
    private sealed class OfflineTransport : IHttpMessageHandlerBuilderFilter
    {
        public ConcurrentQueue<string> Dispatches { get; } = new(); public ConcurrentQueue<string> ProviderCalls { get; } = new();
        public bool DispatchTimeout { get; set; }
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder => { next(builder); builder.PrimaryHandler = new Handler(this); };
        private sealed class Handler(OfflineTransport owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.RequestUri?.Host == "api.github.com" && request.RequestUri.AbsolutePath.EndsWith("/dispatches")) {
                    owner.Dispatches.Enqueue(await request.Content!.ReadAsStringAsync(ct));
                    if (owner.DispatchTimeout) throw new TaskCanceledException("SYNTHETIC_DISPATCH_TIMEOUT");
                    return new(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Get && request.RequestUri?.Host == "api.github.com" &&
                    request.RequestUri.AbsolutePath.StartsWith("/repos/CoderPL1005/portfolio/actions/runs/", StringComparison.Ordinal) &&
                    int.TryParse(request.RequestUri.Segments.Last(), out var runId) && runId is >= 10001 and <= 10002 &&
                    owner.Dispatches.ToArray().ElementAtOrDefault(runId - 10001) is { } dispatch) {
                    var executionId = JsonDocument.Parse(dispatch).RootElement.GetProperty("inputs").GetProperty("execution_id").GetString();
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = runId, run_attempt = 1, head_branch = "main", @event = "workflow_dispatch",
                        path = ".github/workflows/job-discovery-ingestion.yml", status = "in_progress", conclusion = (string?)null,
                        head_sha = "fd620d34e99c0e1398be84ed3f42b13f2fbae143", display_title = "Job Discovery " + executionId,
                        head_repository = new { full_name = "CoderPL1005/portfolio" } }) };
                }
                owner.ProviderCalls.Enqueue(request.RequestUri?.Host ?? "missing");
                throw new InvalidOperationException("TEST_NETWORK_FORBIDDEN");
            }
        }
    }
}
