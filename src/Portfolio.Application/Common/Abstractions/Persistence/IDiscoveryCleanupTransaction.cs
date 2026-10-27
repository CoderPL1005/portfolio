namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IDiscoveryCleanupTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
public interface IDiscoveryCleanupTransactionFactory
{
    Task<IDiscoveryCleanupTransaction> BeginAsync(CancellationToken ct = default);
    Task<IJobDataResetTransaction> BeginResetAllAsync(CancellationToken ct = default);
}
public interface IJobDataResetTransaction : IDiscoveryCleanupTransaction
{
    Task DeleteAllJobDataAsync(CancellationToken ct = default);
}
