namespace Portfolio.Application.Common.Abstractions.Chat;

public interface IChatQuotaService
{
    Task ReserveAsync(
        Guid publicSessionId,
        string visitorKey,
        CancellationToken cancellationToken = default);
}
