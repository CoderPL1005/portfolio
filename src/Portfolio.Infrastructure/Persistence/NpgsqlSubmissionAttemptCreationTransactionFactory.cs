using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlSubmissionAttemptCreationTransactionFactory(ApplicationDbContext dbContext)
    : ISubmissionAttemptCreationTransactionFactory
{
    public async Task<ISubmissionAttemptCreationTransaction> BeginAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var application = await dbContext.JobApplications
                .FromSqlInterpolated($"SELECT * FROM job_applications WHERE id = {applicationId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            var managedCv = application is null ? null : await dbContext.JobApplicationDocuments.AsNoTracking()
                .SingleOrDefaultAsync(document => document.JobApplicationId == application.Id &&
                    document.DocumentType == "CV" && document.PackageRevision == 1 && document.RemovedAt == null,
                    cancellationToken);
            return new CreationTransaction(transaction, application, managedCv);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class CreationTransaction(IDbContextTransaction transaction, JobApplication? application, JobApplicationDocument? managedCv)
        : ISubmissionAttemptCreationTransaction
    {
        public JobApplication? Application { get; } = application;
        public JobApplicationDocument? ManagedCv { get; } = managedCv;
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
