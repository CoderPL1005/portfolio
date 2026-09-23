using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlSubmissionAttemptExecutionTransactionFactory(ApplicationDbContext dbContext)
    : ISubmissionAttemptExecutionTransactionFactory
{
    public async Task<ISubmissionAttemptExecutionTransaction> BeginAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var applicationId = await dbContext.SubmissionAttempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId)
                .Select(attempt => (Guid?)attempt.JobApplicationId)
                .SingleOrDefaultAsync(cancellationToken);
            if (applicationId is null)
                return new ExecutionTransaction(dbContext, transaction, null, null, null);

            // All submission transactions acquire application before attempt to keep lock order deterministic.
            var application = await dbContext.JobApplications
                .FromSqlInterpolated($"SELECT * FROM job_applications WHERE id = {applicationId.Value} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            var attempt = await dbContext.SubmissionAttempts
                .FromSqlInterpolated($"SELECT * FROM submission_attempts WHERE id = {attemptId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            var managedCv = application is null ? null : await dbContext.JobApplicationDocuments.AsNoTracking()
                .SingleOrDefaultAsync(document => document.JobApplicationId == application.Id &&
                    document.DocumentType == "CV" && document.PackageRevision == 1 && document.RemovedAt == null,
                    cancellationToken);
            return new ExecutionTransaction(dbContext, transaction, attempt, application, managedCv);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionTransaction(
        ApplicationDbContext dbContext,
        IDbContextTransaction transaction,
        SubmissionAttempt? attempt,
        JobApplication? application,
        JobApplicationDocument? managedCv) : ISubmissionAttemptExecutionTransaction
    {
        public SubmissionAttempt? Attempt { get; } = attempt;
        public JobApplication? Application { get; } = application;
        public JobApplicationDocument? ManagedCv { get; } = managedCv;
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            dbContext.ChangeTracker.Clear();
        }
    }
}
