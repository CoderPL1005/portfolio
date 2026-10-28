using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Portfolio.Infrastructure.Persistence;

// Release-only startup: existing production database, exact reviewed additive migrations.
public static class DiscoveryReleaseMigration
{
    public const string Candidates = "20261026074534_AddDiscoveryReviewCandidates";
    public const string Reviews = "20261026102500_AddDiscoveryHumanReviews";
    public const string Executions = "20261028121110_AddDiscoveryExecutions";
    public const string Predecessor = "20260923032547_AddSubmissionAttemptCore";
    // Transaction-scoped: safe with Neon transaction pooling; not a session advisory lock.
    public const string LockSql = "SELECT pg_advisory_xact_lock(741204, 445)";

    public static void ValidateHistory(IReadOnlyCollection<string> applied, IReadOnlyCollection<string> pending)
    {
        if (!applied.Contains(Predecessor) || pending.Any(x => x != Candidates && x != Reviews && x != Executions))
            throw new InvalidOperationException("DISCOVERY_MIGRATION_UNEXPECTED_BASELINE");
    }

    public static async Task ApplyAsync(ApplicationDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(90);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '60s'", ct);
        await db.Database.ExecuteSqlRawAsync(LockSql, ct);
        // Re-read history AFTER the database lock: a parallel instance may already have committed.
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        ValidateHistory(applied, pending);
        if (pending.Length > 0) await db.Database.MigrateAsync(ct);
        var remaining = await db.Database.GetPendingMigrationsAsync(ct);
        if (remaining.Any()) throw new InvalidOperationException("DISCOVERY_MIGRATION_INCOMPLETE");
        await transaction.CommitAsync(ct);
    }

    // Startup logs must not include connection strings, SQL parameters or provider exception text.
    public static string SafeFailureCode(Exception e) => e is PostgresException p ? "POSTGRES_" + p.SqlState
        : e is NpgsqlException ? "DATABASE_CONNECTION_FAILED"
        : e is OperationCanceledException ? "STARTUP_TIMEOUT"
        : e is InvalidOperationException { Message: "DISCOVERY_MIGRATION_UNEXPECTED_BASELINE" } ? "UNEXPECTED_BASELINE"
        : "MIGRATION_FAILED";
}
