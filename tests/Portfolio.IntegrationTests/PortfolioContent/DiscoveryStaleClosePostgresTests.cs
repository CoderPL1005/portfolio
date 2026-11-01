using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

// These run only on an explicitly opted-in, disposable *_tests PostgreSQL service.
// LocalRecoveryDatabase creates a fresh schema for each test and never connects to Neon.
public sealed class DiscoveryStaleClosePostgresTests
{
    private const string RunId = "40843461525";
    private const int InitialVersion = 29;
    private static readonly Guid AdminId = Guid.Parse("7a525da9-df4d-4f30-a9da-a274d239403d");

    [RecoveryPostgresFact]
    public async Task RealPostgres_close_preserves_observations_accounting_and_prevents_replay()
    {
        await using var pg = await LocalRecoveryDatabase.Create();
        var id = await Seed(pg);
        await using (var db = pg.Context())
        {
            var result = await Service(db).CloseStaleAsync(id, ValidRequest(), default);
            Assert.Contains("PARTIAL", JsonSerializer.Serialize(result));
        }
        await using (var verify = pg.Context())
        {
            var execution = await verify.DiscoveryExecutions.SingleAsync(x => x.Id == id);
            Assert.Equal("PARTIAL", execution.State);
            Assert.Equal("DISCOVERY_STALE_CLOSED_FOR_REVIEW", execution.ErrorCode);
            Assert.Null(execution.ActiveKey);
            Assert.Null(execution.ResultHash);
            Assert.Equal(InitialVersion + 1, execution.Version);
            Assert.Equal(3, execution.SemanticAttempts);
            Assert.Equal(7, await verify.DiscoveryExecutionObservations.CountAsync(x => x.ExecutionId == id));
            Assert.Equal(3, await verify.DiscoveryExecutionObservations.CountAsync(x => x.ExecutionId == id && x.SemanticState == "UNKNOWN"));
            var attempt = await verify.DiscoveryExecutionAttempts.SingleAsync(x => x.ExecutionId == id);
            Assert.Equal(1, attempt.ActualRequests);
            Assert.Equal(1m, attempt.CreditsDeducted);
            Assert.Equal("CLAIMED", attempt.Outcome);
            Assert.Equal(1, await verify.DiscoveryExecutionEvents.CountAsync(x => x.ExecutionId == id && x.State == "PROVIDER_EVIDENCE"));
            Assert.Equal(1, await verify.DiscoveryExecutionEvents.CountAsync(x => x.ExecutionId == id && x.State == "STALE_CLOSE_AUDIT"));
            Assert.Empty(await verify.DiscoveryCandidates.ToListAsync());
            Assert.Empty(await verify.JobPostings.ToListAsync());
        }
        await using var replayDb = pg.Context();
        await Assert.ThrowsAsync<ConflictException>(() => Service(replayDb).CloseStaleAsync(id, ValidRequest(), default));
    }

    [RecoveryPostgresFact]
    public async Task RealPostgres_audit_insert_failure_rolls_back_execution_update()
    {
        await using var pg = await LocalRecoveryDatabase.Create();
        var id = await Seed(pg);
        var before = await Snapshot(pg, id);
        await using (var db = pg.Context())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_stale_close_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.state = 'STALE_CLOSE_AUDIT' THEN
                        RAISE EXCEPTION 'SYNTHETIC_STALE_CLOSE_AUDIT_FAILURE';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_stale_close_audit_insert
                AFTER INSERT ON discovery_execution_events
                FOR EACH ROW EXECUTE FUNCTION reject_stale_close_audit();
                """);
        }
        await using (var db = pg.Context())
            await Assert.ThrowsAsync<ConflictException>(() => Service(db).CloseStaleAsync(id, ValidRequest(), default));
        Assert.Equal(before, await Snapshot(pg, id));
    }

    [RecoveryPostgresFact]
    public async Task RealPostgres_concurrent_close_requests_have_only_one_winner()
    {
        await using var pg = await LocalRecoveryDatabase.Create();
        var id = await Seed(pg);
        async Task<bool> TryClose()
        {
            await using var db = pg.Context();
            try { await Service(db).CloseStaleAsync(id, ValidRequest(), default); return true; }
            catch (ConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(TryClose(), TryClose());
        Assert.Single(outcomes.Where(x => x));
        await using var verify = pg.Context();
        Assert.Equal("PARTIAL", (await verify.DiscoveryExecutions.SingleAsync(x => x.Id == id)).State);
        Assert.Equal(1, await verify.DiscoveryExecutionEvents.CountAsync(x => x.ExecutionId == id && x.State == "STALE_CLOSE_AUDIT"));
        Assert.Equal(7, await verify.DiscoveryExecutionObservations.CountAsync(x => x.ExecutionId == id));
    }

    [RecoveryPostgresFact]
    public async Task RealPostgres_wrong_version_and_false_attestation_do_not_change_data()
    {
        await using var pg = await LocalRecoveryDatabase.Create();
        var id = await Seed(pg);
        var before = await Snapshot(pg, id);
        await using (var db = pg.Context())
        {
            await Assert.ThrowsAsync<ConflictException>(() => Service(db).CloseStaleAsync(id, ValidRequest() with { ExpectedVersion = 28 }, default));
            await Assert.ThrowsAsync<ValidationException>(() => Service(db).CloseStaleAsync(id, ValidRequest() with { RunConfirmedFinished = false }, default));
            await Assert.ThrowsAsync<ConflictException>(() => Service(db).CloseStaleAsync(id, ValidRequest() with { ExpectedWorkflowRunId = "40843461526" }, default));
        }
        Assert.Equal(before, await Snapshot(pg, id));
    }

    [RecoveryPostgresFact]
    public async Task RealPostgres_provider_evidence_mismatch_blocks_close()
    {
        await using var pg = await LocalRecoveryDatabase.Create();
        var id = await Seed(pg);
        await using (var db = pg.Context())
        {
            var evt = await db.DiscoveryExecutionEvents.SingleAsync(x => x.ExecutionId == id && x.State == "PROVIDER_EVIDENCE");
            evt.Reason = JsonSerializer.Serialize(new { actualRequests = 1, creditsDeducted = 2, httpStatus = 200 });
            await db.SaveChangesAsync();
        }
        var before = await Snapshot(pg, id);
        await using (var db = pg.Context())
            await Assert.ThrowsAsync<ConflictException>(() => Service(db).CloseStaleAsync(id, ValidRequest(), default));
        Assert.Equal(before, await Snapshot(pg, id));
    }

    private static DiscoveryStaleCloseRequest ValidRequest() => new(InitialVersion, RunId,
        "CLOSE STALE EXECUTION WITHOUT RETRY",
        "Test-only confirmation: synthetic GitHub run ended; no provider requests or real user data.", true);

    private static DiscoveryExecutionRecovery Service(ApplicationDbContext db) => new(db,
        new DiscoveryRecoveryTransactions(db), new Unready(), new Actor(), TimeProvider.System);

    private static async Task<Guid> Seed(LocalRecoveryDatabase pg)
    {
        var id = Guid.NewGuid();
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        await using var db = pg.Context();
        db.DiscoveryExecutions.Add(new DiscoveryExecution
        {
            Id = id, ClientRequestId = Guid.NewGuid(), State = "INGESTING", ActiveKey = "DISCOVERY",
            WorkflowRunId = RunId, Version = InitialVersion, SemanticAttempts = 3,
            CreatedAt = old, UpdatedAt = old
        });
        db.DiscoveryExecutionAttempts.Add(new DiscoveryExecutionAttempt
        {
            Id = id, ExecutionId = id, WorkflowRunId = RunId, Outcome = "CLAIMED",
            ActualRequests = 1, CreditsDeducted = 1m, HttpStatus = 200, ClaimedAt = old
        });
        db.DiscoveryExecutionEvents.Add(new DiscoveryExecutionEvent
        {
            Id = Guid.NewGuid(), ExecutionId = id, State = "PROVIDER_EVIDENCE", At = old,
            Reason = JsonSerializer.Serialize(new { actualRequests = 1, creditsDeducted = 1, httpStatus = 200 })
        });
        for (var i = 0; i < 7; i++)
            db.DiscoveryExecutionObservations.Add(new DiscoveryExecutionObservation
            {
                Id = Guid.NewGuid(), ExecutionId = id, ItemIndex = i,
                PayloadJson = "{}", PayloadHash = new string('a', 64),
                SemanticState = i is 0 or 2 or 5 ? "UNKNOWN" : "NOT_REQUESTED"
            });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<string> Snapshot(LocalRecoveryDatabase pg, Guid id)
    {
        await using var db = pg.Context();
        var e = await db.DiscoveryExecutions.AsNoTracking().SingleAsync(x => x.Id == id);
        var attempt = await db.DiscoveryExecutionAttempts.AsNoTracking().SingleAsync(x => x.ExecutionId == id);
        var observations = await db.DiscoveryExecutionObservations.AsNoTracking().Where(x => x.ExecutionId == id)
            .OrderBy(x => x.ItemIndex).Select(x => new { x.Id, x.ItemIndex, x.SemanticState, x.PayloadHash }).ToArrayAsync();
        var events = await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.ExecutionId == id)
            .OrderBy(x => x.State).Select(x => new { x.Id, x.State, x.Reason }).ToArrayAsync();
        return JsonSerializer.Serialize(new
        {
            e.State, e.ActiveKey, e.Version, e.ErrorCode, e.ResultHash, e.SemanticAttempts,
            attempt.ActualRequests, attempt.CreditsDeducted, attempt.Outcome,
            observations, events,
            candidates = await db.DiscoveryCandidates.CountAsync(),
            postings = await db.JobPostings.CountAsync()
        });
    }

    private sealed class Unready : IDiscoveryRecoveryReadiness { public bool CanFinalize => false; }
    private sealed class Actor : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? AdminUserId => AdminId;
        public string? Email => null;
    }
}