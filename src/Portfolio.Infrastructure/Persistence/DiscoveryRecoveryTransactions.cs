using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Infrastructure.Persistence;

public sealed class DiscoveryRecoveryReadiness(IHostEnvironment environment, IOptions<DiscoveryRecoveryOptions> options) : IDiscoveryRecoveryReadiness
{
    public bool CanFinalize => !environment.IsProduction() || options.Value is { ProductionFinalizeEnabled: true, ProjectionReady: true,
        PostgreSqlVerificationReference: { Length: > 0 and <= 300 } };
}

public sealed class DiscoveryRecoveryTransactions(IApplicationDbContext context) : IDiscoveryRecoveryTransactions
{
    // Application-only fallback for an explicitly injected nonrelational test DB.
    // It is never used as PostgreSQL/production transaction evidence.
    private static readonly SemaphoreSlim NonRelational = new(1, 1);
    public async Task<IDiscoveryRecoveryTransaction> BeginAsync(bool write, CancellationToken ct)
    {
        if (context is not DbContext db) throw new InvalidOperationException("Recovery requires a database context.");
        if (!db.Database.IsRelational()) {
            await NonRelational.WaitAsync(ct); return new Lease();
        }
        var tx = await db.Database.BeginTransactionAsync(write ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead, ct);
        try {
            if (write) {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
                // Includes all independently mutable preview dependencies and
                // prevents phantoms, imports, reviews and Reset All during commit.
                await db.Database.ExecuteSqlRawAsync("""
                    LOCK TABLE discovery_executions, discovery_execution_attempts,
                    discovery_execution_observations, raw_job_postings,
                    discovery_execution_vacancies, job_postings, discovery_candidates,
                    discovery_candidate_reviews, discovery_candidate_review_events,
                    discovery_execution_events IN SHARE ROW EXCLUSIVE MODE
                    """, ct);
            }
            else await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            return new Relational(tx);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    private sealed class Lease : IDiscoveryRecoveryTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() { NonRelational.Release(); return ValueTask.CompletedTask; }
    }
    private sealed class Relational(IDbContextTransaction tx) : IDiscoveryRecoveryTransaction
    {
        public Task CommitAsync(CancellationToken ct) => tx.CommitAsync(ct);
        public ValueTask DisposeAsync() => tx.DisposeAsync();
    }
}
