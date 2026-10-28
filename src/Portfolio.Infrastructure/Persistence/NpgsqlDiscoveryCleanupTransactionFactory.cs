using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlDiscoveryCleanupTransactionFactory(ApplicationDbContext db) : IDiscoveryCleanupTransactionFactory
{
    public async Task<IDiscoveryCleanupTransaction> BeginAsync(CancellationToken ct = default) => await BeginCoreAsync(false, ct);
    public async Task<IJobDataResetTransaction> BeginResetAllAsync(CancellationToken ct = default) => await BeginCoreAsync(true, ct);
    private async Task<IJobDataResetTransaction> BeginCoreAsync(bool allJobs, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialize cleanup and block concurrent INSERT/UPDATE/DELETE, including
            // phantom imports and first reviews. Ordinary SELECT remains available.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync(allJobs
                ? "LOCK TABLE discovery_executions, discovery_execution_attempts, discovery_execution_events, discovery_execution_observations, discovery_execution_vacancies, job_postings, job_applications, raw_job_postings, submission_attempts, submission_attempt_events, job_application_documents, job_application_events, raw_job_posting_attachments, discovery_candidates, discovery_candidate_reviews, discovery_candidate_review_events IN SHARE ROW EXCLUSIVE MODE"
                : "LOCK TABLE discovery_candidates, discovery_candidate_reviews, discovery_candidate_review_events IN SHARE ROW EXCLUSIVE MODE", ct);
            if(allJobs)await JobDataReset.RequireInactiveExecutionAsync(db,ct);
            return new Transaction(tx, db, allJobs);
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    private sealed class Transaction(IDbContextTransaction tx, ApplicationDbContext db, bool allJobs) : IJobDataResetTransaction
    {
        public async Task DeleteAllJobDataAsync(CancellationToken ct = default)
        {
            if (!allJobs) throw new InvalidOperationException("Reset-all requires the full job-data transaction scope.");
            await JobDataReset.RequireInactiveExecutionAsync(db,ct);
            // Static, explicit FK order; never TRUNCATE/CASCADE or touch unrelated tables.
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM discovery_candidate_review_events;
                DELETE FROM discovery_candidate_reviews;
                DELETE FROM discovery_candidates;
                DELETE FROM discovery_execution_vacancies;
                DELETE FROM discovery_execution_observations;
                DELETE FROM submission_attempt_events;
                DELETE FROM submission_attempts;
                DELETE FROM job_application_documents;
                DELETE FROM job_application_events;
                DELETE FROM job_applications;
                DELETE FROM raw_job_posting_attachments;
                UPDATE raw_job_postings SET duplicate_of_raw_job_posting_id = NULL WHERE duplicate_of_raw_job_posting_id IS NOT NULL;
                DELETE FROM raw_job_postings;
                DELETE FROM job_postings;
                """, ct);
        }
        public Task CommitAsync(CancellationToken ct = default) => tx.CommitAsync(ct);
        public ValueTask DisposeAsync() => tx.DisposeAsync();
    }
}
