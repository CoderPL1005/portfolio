using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlJobApplicationCreationTransactionFactory(ApplicationDbContext dbContext)
    : IJobApplicationCreationTransactionFactory
{
    public async Task<IJobApplicationCreationTransaction> BeginAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var jobPosting = await dbContext.JobPostings
                .FromSqlInterpolated($"SELECT * FROM job_postings WHERE id = {jobPostingId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            return new CreationTransaction(transaction, jobPosting);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class CreationTransaction(
        IDbContextTransaction transaction,
        JobPosting? jobPosting) : IJobApplicationCreationTransaction
    {
        public JobPosting? JobPosting { get; } = jobPosting;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
