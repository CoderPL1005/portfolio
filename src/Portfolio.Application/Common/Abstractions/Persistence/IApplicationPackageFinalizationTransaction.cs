using Portfolio.Domain.Entities;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IApplicationPackageFinalizationTransaction : IAsyncDisposable
{
    JobApplication? Application { get; }
    JobPosting? JobPosting { get; }
    CanonicalCv? CanonicalCv { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationPackageFinalizationTransactionFactory
{
    Task<IApplicationPackageFinalizationTransaction> BeginAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
