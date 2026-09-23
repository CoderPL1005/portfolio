using Portfolio.Domain.Entities;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface ISubmissionAttemptExecutionTransaction : IAsyncDisposable
{
    SubmissionAttempt? Attempt { get; }
    JobApplication? Application { get; }
    JobApplicationDocument? ManagedCv { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ISubmissionAttemptExecutionTransactionFactory
{
    Task<ISubmissionAttemptExecutionTransaction> BeginAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
}
