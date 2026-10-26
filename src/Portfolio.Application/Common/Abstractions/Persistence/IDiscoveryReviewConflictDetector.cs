namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IDiscoveryReviewConflictDetector
{
    bool IsReviewConflict(Exception exception);
}
