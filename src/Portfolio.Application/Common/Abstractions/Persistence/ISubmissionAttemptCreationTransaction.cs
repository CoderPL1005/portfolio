using Portfolio.Domain.Entities;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface ISubmissionAttemptCreationTransaction : IAsyncDisposable
{
    JobApplication? Application { get; }
    JobApplicationDocument? ManagedCv { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ISubmissionAttemptCreationTransactionFactory
{
    Task<ISubmissionAttemptCreationTransaction> BeginAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
