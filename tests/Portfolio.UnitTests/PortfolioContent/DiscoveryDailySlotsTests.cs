using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryDailySlotsTests
{
    [Theory]
    [InlineData("0 0 * * *", 0, 0, 1)]
    [InlineData("30 0 * * *", 0, 30, 1)]
    [InlineData("0 3 * * *", 3, 0, 2)]
    [InlineData("30 3 * * *", 3, 30, 2)]
    [InlineData("0 6 * * *", 6, 0, 3)]
    [InlineData("30 6 * * *", 6, 30, 3)]
    [InlineData("0 9 * * *", 9, 0, 4)]
    [InlineData("30 9 * * *", 9, 30, 4)]
    [InlineData("0 12 * * *", 12, 0, 5)]
    [InlineData("30 12 * * *", 12, 30, 5)]
    [InlineData("0 15 * * *", 15, 0, 6)]
    public void All_eleven_exact_hour_and_fallback_expressions_resolve_to_planned_business_slots(string expression, int hour, int triggerMinute, int expectedSlot)
    {
        Assert.True(DiscoveryDailySlots.IsKnownExpression(expression));
        var now = new DateTimeOffset(2026, 11, 2, hour, triggerMinute, 0, TimeSpan.Zero);
        Assert.True(DiscoveryDailySlots.TryResolve(expression, now, out var planned));
        Assert.Equal(hour, planned.Hour);
        Assert.Equal(0, planned.Minute);
        Assert.Equal(TimeSpan.Zero, planned.Offset);
        Assert.Equal(expectedSlot, DiscoveryDailySlots.SlotNumber(planned));
        Assert.Equal(DiscoveryDailySlots.RequestId(planned), DiscoveryDailySlots.RequestId(planned));
    }

    [Theory]
    [InlineData(0, 1, "2026-11-01", "2026-11-02")]
    [InlineData(3, 2, "2026-11-01", "2026-11-02")]
    [InlineData(6, 3, "2026-11-02", "2026-11-02")]
    [InlineData(9, 4, "2026-11-02", "2026-11-02")]
    [InlineData(12, 5, "2026-11-02", "2026-11-02")]
    public void Windows_follow_local_Vietnam_calendar(int utcHour, int slot, string start, string end)
    {
        var when = new DateTimeOffset(2026, 11, 2, utcHour, 0, 0, TimeSpan.Zero);
        var window = DiscoveryDailySlots.Window(when);
        Assert.Equal(slot, window.Slot);
        Assert.Equal(start, window.StartTime);
        Assert.Equal(end, window.EndTime);
        Assert.Equal("Asia/Ho_Chi_Minh", window.TimeZone);
        Assert.Equal(window, DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(when), when));
        Assert.Equal(window, DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(when), when.AddMinutes(30)));
    }

    [Theory]
    [InlineData("2026-12-31T17:00:00Z", 1, "2026-12-31", "2027-01-01", "2027-01-01")] // 00:00 Jan 1, 2027 ICT
    [InlineData("2027-01-01T00:00:00Z", 1, "2026-12-31", "2027-01-01", "2027-01-01")] // 07:00 Jan 1, 2027 ICT
    [InlineData("2027-01-01T06:00:00Z", 3, "2027-01-01", "2027-01-01", "2027-01-01")] // 13:00 Jan 1, 2027 ICT
    [InlineData("2026-03-01T00:00:00Z", 1, "2026-02-28", "2026-03-01", "2026-03-01")] // 07:00 Mar 1, 2026 ICT
    [InlineData("2026-03-01T06:00:00Z", 3, "2026-03-01", "2026-03-01", "2026-03-01")] // 13:00 Mar 1, 2026 ICT
    public void Vietnam_calendar_handles_month_and_year_rollover_and_utc_day_differences(
        string utcIso, int slot, string expectedStart, string expectedEnd, string expectedBusinessDate)
    {
        var instant = DateTimeOffset.Parse(utcIso);
        Assert.Equal(expectedBusinessDate, DiscoveryDailySlots.VietnamBusinessDate(instant));
        var bounds = DiscoveryDailySlots.VietnamBusinessDayBoundsUtc(instant);
        Assert.Equal(TimeSpan.FromHours(24), bounds.EndUtc - bounds.StartUtc);
        Assert.Equal(17, bounds.StartUtc.Hour); // 00:00+07:00 is 17:00 UTC previous calendar day
        if (instant.Hour is 0 or 3 or 6 or 9 or 12)
        {
            var window = DiscoveryDailySlots.Window(instant);
            Assert.Equal(slot, window.Slot);
            Assert.Equal(expectedStart, window.StartTime);
            Assert.Equal(expectedEnd, window.EndTime);
        }
    }

    [Fact]
    public void Sixth_slot_must_not_create_a_search_window()
    {
        var planned = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(6, DiscoveryDailySlots.SlotNumber(planned));
        Assert.Throws<ArgumentException>(() => DiscoveryDailySlots.Window(planned));
        Assert.Throws<ArgumentException>(() => DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(planned), planned.AddMinutes(18)));
    }

    [Theory]
    [InlineData("30 0 * * *", 0, 0)]
    [InlineData("30 0 * * *", 0, 29)]
    [InlineData("30 3 * * *", 3, 15)]
    [InlineData("30 9 * * *", 9, 29)]
    [InlineData("0 3 * * *", 2, 59)]
    [InlineData("0 15 * * *", 14, 59)]
    public void Rejects_early_late_and_unlisted_schedule(string expression, int hour, int earlyMinute)
    {
        var earlyInSlot = new DateTimeOffset(2026, 11, 2, hour, earlyMinute, 59, TimeSpan.Zero);
        Assert.False(DiscoveryDailySlots.TryResolve(expression, earlyInSlot, out _));

        var dayBefore = new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.Zero);
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *", dayBefore, out _));
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *", dayBefore.AddMinutes(92), out _));

        foreach (var invalid in new[]
        {
            "11 0 * * *", "41 0 * * *", "11 3 * * *", "41 3 * * *", "11 6 * * *", "41 6 * * *",
            "11 9 * * *", "41 9 * * *", "11 12 * * *", "41 12 * * *", "11 15 * * *", "41 15 * * *",
            "30 15 * * *", "0 1 * * *", "0 0 * * *; touch pwn", ""
        })
        {
            Assert.False(DiscoveryDailySlots.IsKnownExpression(invalid));
            Assert.False(DiscoveryDailySlots.TryResolve(invalid, new DateTimeOffset(2026, 11, 2, 0, 35, 0, TimeSpan.Zero), out _));
        }
        Assert.False(DiscoveryDailySlots.IsKnownExpression(null));
    }

    [Theory]
    [InlineData("0 0 * * *", "30 0 * * *", 0)]
    [InlineData("0 3 * * *", "30 3 * * *", 3)]
    [InlineData("0 6 * * *", "30 6 * * *", 6)]
    [InlineData("0 9 * * *", "30 9 * * *", 9)]
    [InlineData("0 12 * * *", "30 12 * * *", 12)]
    public void Backup_and_primary_share_the_same_request_id_and_window(string primary, string backup, int hour)
    {
        var primaryTime = new DateTimeOffset(2026, 11, 2, hour, 0, 0, TimeSpan.Zero);
        var backupTime = new DateTimeOffset(2026, 11, 2, hour, 30, 0, TimeSpan.Zero);
        Assert.True(DiscoveryDailySlots.IsKnownExpression(primary));
        Assert.True(DiscoveryDailySlots.IsKnownExpression(backup));
        Assert.True(DiscoveryDailySlots.TryResolve(primary, primaryTime, out var plannedPrimary));
        Assert.True(DiscoveryDailySlots.TryResolve(backup, backupTime, out var plannedBackup));
        Assert.True(DiscoveryDailySlots.TryResolve(primary, backupTime, out var plannedPrimaryAtBackupTime));
        Assert.Equal(plannedPrimary, plannedBackup);
        Assert.Equal(plannedPrimary, plannedPrimaryAtBackupTime);
        Assert.Equal(DiscoveryDailySlots.RequestId(plannedPrimary), DiscoveryDailySlots.RequestId(plannedBackup));
        Assert.Equal(DiscoveryDailySlots.Window(plannedPrimary), DiscoveryDailySlots.Window(plannedBackup));
        Assert.Equal(DiscoveryDailySlots.Window(plannedBackup),
            DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(plannedBackup), backupTime));
    }

    [Fact]
    public void Delayed_cron_is_known_but_cannot_create_an_execution_or_roll_into_next_slot()
    {
        var nextSlotTime = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
        Assert.True(DiscoveryDailySlots.IsKnownExpression("0 0 * * *"));
        Assert.True(DiscoveryDailySlots.IsKnownExpression("30 0 * * *"));
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *", nextSlotTime, out var expiredPlanned));
        Assert.Equal(0, expiredPlanned.Hour); // Remains slot 1 (00:00 UTC), never rolls into slot 2 (03:00 UTC)
        Assert.False(DiscoveryDailySlots.TryResolve("30 0 * * *", nextSlotTime, out _));
        Assert.False(DiscoveryDailySlots.IsKnownExpression("30 15 * * *")); // no backup for the zero-cost reserved slot
    }

    [Fact]
    public void Each_slot_and_day_has_distinct_stable_request_id()
    {
        var day = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var keys = Enumerable.Range(0, 6).Select(i => DiscoveryDailySlots.RequestId(day.AddHours(i * 3))).ToArray();
        Assert.Equal(6, keys.Distinct().Count());
        Assert.NotEqual(keys[0], DiscoveryDailySlots.RequestId(day.AddDays(1)));
    }

    [Fact]
    public async Task Concurrent_primary_and_fallback_create_single_execution_and_preserve_credit_and_ingestion_history()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seedDb = CreateDatabase(dbName);
        var priorExecId = Guid.NewGuid();
        var rawPostId = Guid.NewGuid();
        await SeedPortfolioAndHistoryAsync(seedDb, priorExecId, rawPostId);

        var clock = new FixedClock(new DateTimeOffset(2026, 11, 2, 0, 30, 0, TimeSpan.Zero));
        var github = new CountingGitHubClient();
        await using var dbPrimary = CreateDatabase(dbName);
        await using var dbFallback = CreateDatabase(dbName);
        var primaryService = CreateService(dbPrimary, github, clock);
        var fallbackService = CreateService(dbFallback, github, clock);

        var results = await Task.WhenAll(
            primaryService.StartScheduledAsync("0 0 * * *"),
            fallbackService.StartScheduledAsync("30 0 * * *"));

        var states = results.Select(GetState).OrderBy(x => x).ToArray();
        Assert.Equal(["ALREADY_CREATED", "QUEUED"], states);
        var execIds = results.Select(GetExecutionId).ToArray();
        Assert.NotNull(execIds[0]);
        Assert.Equal(execIds[0], execIds[1]);
        Assert.Equal(1, github.DispatchCount);

        // Reserved slot 6, expired trigger, and unlisted expression checks:
        clock.Now = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
        var reserved = await primaryService.StartScheduledAsync("0 15 * * *");
        Assert.Equal("RESERVED_FUTURE_CV_APPLICATION_THRESHOLD", GetState(reserved));
        Assert.Equal(1, github.DispatchCount);

        var expired = await primaryService.StartScheduledAsync("0 0 * * *");
        Assert.Equal("SKIPPED_EXPIRED", GetState(expired));
        Assert.Equal(1, github.DispatchCount);

        await Assert.ThrowsAsync<ValidationException>(() => primaryService.StartScheduledAsync("11 0 * * *"));
        await Assert.ThrowsAsync<ValidationException>(() => primaryService.StartScheduledAsync("41 0 * * *"));
        Assert.Equal(1, github.DispatchCount);

        // Verify credit and ingestion history remained intact:
        await using var verifyDb = CreateDatabase(dbName);
        Assert.Equal(2, await verifyDb.DiscoveryExecutions.CountAsync()); // 1 prior + 1 new slot 1 execution
        var priorAttempt = await verifyDb.DiscoveryExecutionAttempts.SingleAsync(x => x.ExecutionId == priorExecId);
        Assert.Equal(1, priorAttempt.ActualRequests);
        Assert.Equal(1, priorAttempt.CreditsDeducted);
        Assert.Equal(1, await verifyDb.DiscoveryExecutionObservations.CountAsync(x => x.ExecutionId == priorExecId));
        Assert.Equal(1, await verifyDb.DiscoveryCandidates.CountAsync());
        Assert.Equal(1, await verifyDb.RawJobPostings.CountAsync(x => x.Id == rawPostId));
    }

    [Fact]
    public async Task Scheduled_slots_rotate_persisted_queries_within_Vietnam_day_and_freeze_selection_for_fallback()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDatabase(dbName);
        await SeedPortfolioAndHistoryAsync(db, Guid.NewGuid(), Guid.NewGuid());
        var pyId = Guid.NewGuid();
        db.Technologies.Add(new Technology { Id = pyId, Name = "Python", Category = "Backend", IsActive = true });
        db.Skills.Add(new Skill { Id = Guid.NewGuid(), Name = "Python", Category = "Backend", TechnologyId = pyId, ExperienceLevel = "USED", IsPublished = true });
        var prefs = await db.CandidateJobPreferences.SingleAsync();
        prefs.PreferredTechnologies = JsonDocument.Parse("[\"C#\", \".NET\", \"Python\"]");
        prefs.AcceptableLocations = JsonDocument.Parse("[\"Hanoi\", \"Hà Nội\"]");
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        var github = new CountingGitHubClient();
        var service = CreateService(db, github, clock);

        var selectedQueries = new List<string>();
        var cronHours = new[] { (0, "0 0 * * *", "30 0 * * *"), (3, "0 3 * * *", "30 3 * * *"), (6, "0 6 * * *", "30 6 * * *"), (9, "0 9 * * *", "30 9 * * *"), (12, "0 12 * * *", "30 12 * * *") };

        var first = true;
        foreach (var (hour, primaryCron, fallbackCron) in cronHours)
        {
            clock.Now = new DateTimeOffset(2026, 11, 2, hour, 0, 0, TimeSpan.Zero);
            var primaryResult = await service.StartScheduledAsync(primaryCron);
            Assert.Equal("QUEUED", GetState(primaryResult));
            var execId = GetExecutionId(primaryResult)!.Value;
            var exec = await db.DiscoveryExecutions.SingleAsync(x => x.Id == execId);
            using var configDoc = JsonDocument.Parse(exec.ConfigurationJson);
            var query = configDoc.RootElement.GetProperty("query").GetString()!;
            var decision = configDoc.RootElement.GetProperty("querySelection").GetProperty("decision").GetString()!;
            Assert.NotNull(query);
            Assert.Equal(first ? "EXPLORATION_PRIOR" : "ROTATED_UNTRIED_TODAY", decision);
            first = false;
            selectedQueries.Add(query);

            // Simulate fallback at :30 — must return ALREADY_CREATED with identical frozen ConfigurationJson
            clock.Now = new DateTimeOffset(2026, 11, 2, hour, 30, 0, TimeSpan.Zero);
            var fallbackResult = await service.StartScheduledAsync(fallbackCron);
            Assert.Equal("ALREADY_CREATED", GetState(fallbackResult));
            Assert.Equal(execId, GetExecutionId(fallbackResult));
            var execAfterFallback = await db.DiscoveryExecutions.SingleAsync(x => x.Id == execId);
            Assert.Equal(exec.ConfigurationJson, execAfterFallback.ConfigurationJson);

            // Complete execution so next slot can run
            exec.State = "SUCCEEDED";
            exec.ActiveKey = null;
            db.DiscoveryExecutionAttempts.Add(new DiscoveryExecutionAttempt
            {
                Id = execId,
                ExecutionId = execId,
                WorkflowRunId = $"run-{hour}",
                Outcome = "SUCCEEDED",
                ActualRequests = 1,
                CreditsDeducted = 1,
                HttpStatus = 200,
                ClaimedAt = clock.Now
            });
            await db.SaveChangesAsync();
        }

        // All 5 slots in the same Vietnam business day selected distinct queries without repeating query #1
        Assert.Equal(5, selectedQueries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void External_scheduler_has_two_grouped_cloudflare_crons_and_no_github_schedule_trigger()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "job-discovery-daily.yml"));
        var cloudflare = File.ReadAllText(Path.Combine(root, "tools", "cloudflare-job-discovery-scheduler", "wrangler.jsonc"));

        var normalized = workflow.Replace("\r\n", "\n");
        var onSection = normalized.Substring(normalized.IndexOf("on:\n", StringComparison.Ordinal),
            normalized.IndexOf("permissions:\n", StringComparison.Ordinal) - normalized.IndexOf("on:\n", StringComparison.Ordinal));
        Assert.DoesNotContain("  schedule:\n", onSection, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("          - diagnostic_only", workflow, StringComparison.Ordinal);
        Assert.Contains("          - external_scheduled", workflow, StringComparison.Ordinal);
        Assert.Contains("default: 'diagnostic_only'", workflow, StringComparison.Ordinal);
        Assert.Contains("SCHEDULER_RESULT_STATE: SKIPPED_EXPIRED", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.classify.outputs.authorized == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.slot == '0 15 * * *'", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.slot != '0 15 * * *'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("11 15 * * *", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$GITHUB_EVENT_NAME\" = \"workflow_dispatch\"", workflow, StringComparison.Ordinal);
        Assert.Contains("0 0,3,6,9,12,15 * * *", cloudflare, StringComparison.Ordinal);
        Assert.Contains("30 0,3,6,9,12 * * *", cloudflare, StringComparison.Ordinal);
        Assert.DoesNotContain("11 0,3,6,9,12,15 * * *", cloudflare, StringComparison.Ordinal);
        Assert.DoesNotContain("41 0,3,6,9,12 * * *", cloudflare, StringComparison.Ordinal);
        Assert.Contains("\"DISPATCH_ENABLED\": \"false\"", cloudflare, StringComparison.Ordinal);
    }

    private static string GetState(object result) =>
        JsonSerializer.SerializeToElement(result).GetProperty("state").GetString()!;

    private static Guid? GetExecutionId(object result) =>
        JsonSerializer.SerializeToElement(result).TryGetProperty("executionId", out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetGuid()
            : null;

    private static ContentTestDbContext CreateDatabase(string name) =>
        new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name).Options);

    private static DiscoveryExecutionService CreateService(ContentTestDbContext db, IDiscoveryGitHubClient github, TimeProvider clock) =>
        new(db, github, new DisabledSemanticExtractor(), Options.Create(new DiscoveryExecutionOptions
        {
            Enabled = true,
            DailyEnabled = true,
            FreePlanConfirmed = true,
            NoCashConfirmed = true,
            EvidencePolicy = "V2"
        }), new UnauthenticatedUser(), clock);

    private static async Task SeedPortfolioAndHistoryAsync(ContentTestDbContext db, Guid priorExecId, Guid rawPostId)
    {
        var techId = Guid.NewGuid();
        db.Technologies.Add(new Technology { Id = techId, Name = "C#", Category = "Backend", IsActive = true });
        db.Skills.Add(new Skill { Id = Guid.NewGuid(), Name = "ASP.NET Core", Category = "Backend", TechnologyId = techId, ExperienceLevel = "Fresher", IsPublished = true });
        db.CandidateJobPreferences.Add(new CandidateJobPreferences
        {
            Id = Guid.NewGuid(),
            SingletonKey = "CURRENT",
            TargetRoles = JsonDocument.Parse("[\"Backend Developer\"]"),
            PreferredTechnologies = JsonDocument.Parse("[\"C#\"]"),
            AcceptableLocations = JsonDocument.Parse("[\"Hanoi\"]"),
            WorkplaceTypes = JsonDocument.Parse("[\"Onsite\"]"),
            EmploymentTypes = JsonDocument.Parse("[\"FullTime\"]"),
            Version = 1
        });
        var earlier = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
        db.DiscoveryExecutions.Add(new DiscoveryExecution
        {
            Id = priorExecId,
            ClientRequestId = Guid.NewGuid(),
            TriggerSource = "DAILY_SCHEDULED",
            State = "SUCCEEDED",
            ActiveKey = null,
            ConfigurationJson = "{}",
            CreatedAt = earlier,
            UpdatedAt = earlier
        });
        db.DiscoveryExecutionAttempts.Add(new DiscoveryExecutionAttempt
        {
            Id = priorExecId,
            ExecutionId = priorExecId,
            WorkflowRunId = "99999",
            Outcome = "SUCCEEDED",
            ActualRequests = 1,
            CreditsDeducted = 1,
            HttpStatus = 200,
            ClaimedAt = earlier
        });
        db.DiscoveryExecutionObservations.Add(new DiscoveryExecutionObservation
        {
            Id = Guid.NewGuid(),
            ExecutionId = priorExecId,
            ItemIndex = 0,
            PayloadJson = "{}",
            PayloadHash = new string('b', 64),
            SemanticState = "NOT_REQUESTED"
        });
        db.RawJobPostings.Add(new RawJobPosting
        {
            Id = rawPostId,
            RawContent = "Backend .NET Developer",
            ContentHash = new string('c', 64),
            Metadata = JsonDocument.Parse("{}")
        });
        await new ImportDiscoveryReviewHandler(db, TimeProvider.System).HandleAsync(new(DiscoveryReviewFeatureTests.Bundle()));
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingGitHubClient : IDiscoveryGitHubClient
    {
        private int _dispatches;
        public int DispatchCount => _dispatches;
        public Task<string> DispatchAsync(Guid executionId, CancellationToken ct)
        {
            Interlocked.Increment(ref _dispatches);
            return Task.FromResult("ACCEPTED");
        }
        public Task<bool> VerifyRunAsync(Guid executionId, string runId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class DisabledSemanticExtractor : IDiscoveryTextExtractor
    {
        public bool IsConfigured => false;
        public string ModelIdentity => "gemini-disabled";
        public Task<DiscoveryTextGeneration> ExtractAsync(string rawText, CancellationToken ct) =>
            throw new InvalidOperationException("Gemini must not be called in daily scheduler.");
    }

    private sealed class UnauthenticatedUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? AdminUserId => null;
        public string? Email => null;
    }
}
