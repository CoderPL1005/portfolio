using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlApplicationPackageFinalizationTransactionFactory(ApplicationDbContext dbContext)
    : IApplicationPackageFinalizationTransactionFactory
{
    public async Task<IApplicationPackageFinalizationTransaction> BeginAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var application = await dbContext.JobApplications
                .FromSqlInterpolated($"SELECT * FROM job_applications WHERE id = {applicationId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            JobPosting? jobPosting = null;
            CanonicalCv? canonicalCv = null;
            if (application is not null)
            {
                jobPosting = await dbContext.JobPostings
                    .FromSqlInterpolated($"SELECT * FROM job_postings WHERE id = {application.JobPostingId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
                canonicalCv = await dbContext.CanonicalCvs
                    .FromSqlRaw("SELECT * FROM canonical_cvs WHERE singleton_key = 1 FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
            }
            return new FinalizationTransaction(transaction, application, jobPosting, canonicalCv);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class FinalizationTransaction(
        IDbContextTransaction transaction,
        JobApplication? application,
        JobPosting? jobPosting,
        CanonicalCv? canonicalCv) : IApplicationPackageFinalizationTransaction
    {
        public JobApplication? Application { get; } = application;
        public JobPosting? JobPosting { get; } = jobPosting;
        public CanonicalCv? CanonicalCv { get; } = canonicalCv;
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
