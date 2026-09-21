using Portfolio.Domain.Entities;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IJobApplicationCreationTransaction : IAsyncDisposable
{
    JobPosting? JobPosting { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IJobApplicationCreationTransactionFactory
{
    Task<IJobApplicationCreationTransaction> BeginAsync(
        Guid jobPostingId,
        CancellationToken cancellationToken = default);
}
