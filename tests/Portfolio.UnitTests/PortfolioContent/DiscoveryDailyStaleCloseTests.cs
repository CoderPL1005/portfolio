using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryDailyStaleCloseTests
{
    [Fact]
    public async Task Old_exhausted_execution_can_be_closed_only_by_explicit_operator_and_preserves_accounting()
    {
        await using var db = CreateDatabase();
        var id = Guid.NewGuid();
        await Seed(db, id, DateTimeOffset.UtcNow.AddDays(-3));
        var service = new DiscoveryExecutionRecovery(db, new FakeTransactions(), new Unready(), new Admin(), TimeProvider.System);
        var request = new DiscoveryStaleCloseRequest(5, "10001", "CLOSE STALE EXECUTION WITHOUT RETRY",
            "Workflow concluded. Operator verified the GitHub Actions run is no longer executing.", true);
        var result = await service.CloseStaleAsync(id, request, default);
        var execution = await db.DiscoveryExecutions.SingleAsync();
        Assert.Equal("PARTIAL", execution.State);
        Assert.Equal("DISCOVERY_STALE_CLOSED_FOR_REVIEW", execution.ErrorCode);
        Assert.Null(execution.ActiveKey);
        Assert.Null(execution.ResultHash);
        Assert.Equal(3, execution.SemanticAttempts);
        Assert.Equal(7, await db.DiscoveryExecutionObservations.CountAsync());
        Assert.Equal(3, await db.DiscoveryExecutionObservations.CountAsync(x => x.SemanticState == "UNKNOWN"));
        var attempt = await db.DiscoveryExecutionAttempts.SingleAsync();
        Assert.Equal("CLAIMED", attempt.Outcome);
        Assert.Equal(1, attempt.ActualRequests);
        Assert.Equal(1, attempt.CreditsDeducted);
        Assert.Empty(await db.DiscoveryCandidates.ToListAsync());
        Assert.Empty(await db.JobPostings.ToListAsync());
        Assert.Single(await db.DiscoveryExecutionEvents.Where(x => x.State == "STALE_CLOSE_AUDIT").ToListAsync());
        await Assert.ThrowsAsync<ConflictException>(() => service.CloseStaleAsync(id, request, default));
    }

    [Fact]
    public async Task Recent_execution_cannot_be_closed_even_with_operator_confirmation()
    {
        await using var db = CreateDatabase();
        var id = Guid.NewGuid();
        await Seed(db, id, DateTimeOffset.UtcNow.AddMinutes(-10));
        var service = new DiscoveryExecutionRecovery(db, new FakeTransactions(), new Unready(), new Admin(), TimeProvider.System);
        await Assert.ThrowsAsync<ConflictException>(() => service.CloseStaleAsync(id,
            new(5, "10001", "CLOSE STALE EXECUTION WITHOUT RETRY", "Explicit operator reason and confirmed finished run.", true), default));
        Assert.NotNull((await db.DiscoveryExecutions.SingleAsync()).ActiveKey);
    }

    [Fact]
    public async Task False_or_stale_operator_confirmation_never_releases_lock()
    {
        await using var db = CreateDatabase();
        var id = Guid.NewGuid();
        await Seed(db, id, DateTimeOffset.UtcNow.AddDays(-3));
        var service = new DiscoveryExecutionRecovery(db, new FakeTransactions(), new Unready(), new Admin(), TimeProvider.System);
        await Assert.ThrowsAsync<ValidationException>(() => service.CloseStaleAsync(id,
            new(5, "10001", "CLOSE STALE EXECUTION WITHOUT RETRY", "Operator has NOT confirmed this run finished.", false), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.CloseStaleAsync(id,
            new(4, "10001", "CLOSE STALE EXECUTION WITHOUT RETRY", "Operator confirmed the external GitHub run completed.", true), default));
        Assert.Equal("INGESTING", (await db.DiscoveryExecutions.SingleAsync()).State);
    }

    private static ContentTestDbContext CreateDatabase() => new(new DbContextOptionsBuilder<ContentTestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task Seed(ContentTestDbContext db, Guid id, DateTimeOffset updated)
    {
        db.DiscoveryExecutions.Add(new DiscoveryExecution
        {
            Id = id, ClientRequestId = Guid.NewGuid(), State = "INGESTING", ActiveKey = "DISCOVERY",
            WorkflowRunId = "10001", Version = 5, SemanticAttempts = 3,
            CreatedAt = updated, UpdatedAt = updated
        });
        db.DiscoveryExecutionAttempts.Add(new DiscoveryExecutionAttempt
        {
            Id = id, ExecutionId = id, WorkflowRunId = "10001", Outcome = "CLAIMED",
            ActualRequests = 1, CreditsDeducted = 1, HttpStatus = 200, ClaimedAt = updated
        });
        db.DiscoveryExecutionEvents.Add(new DiscoveryExecutionEvent
        {
            Id = Guid.NewGuid(), ExecutionId = id, State = "PROVIDER_EVIDENCE", At = updated,
            Reason = JsonSerializer.Serialize(new { actualRequests = 1, creditsDeducted = 1, httpStatus = 200 })
        });
        for (var i = 0; i < 7; i++) db.DiscoveryExecutionObservations.Add(new DiscoveryExecutionObservation
        {
            Id = Guid.NewGuid(), ExecutionId = id, ItemIndex = i, PayloadJson = "{}", PayloadHash = new string('a', 64),
            SemanticState = i is 0 or 2 or 5 ? "UNKNOWN" : "NOT_REQUESTED"
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeTransactions : IDiscoveryRecoveryTransactions
    {
        public Task<IDiscoveryRecoveryTransaction> BeginAsync(bool write, CancellationToken ct) => Task.FromResult<IDiscoveryRecoveryTransaction>(new Lease());
        private sealed class Lease : IDiscoveryRecoveryTransaction
        {
            public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Unready : IDiscoveryRecoveryReadiness { public bool CanFinalize => false; }
    private sealed class Admin : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? AdminUserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => null;
    }
}
