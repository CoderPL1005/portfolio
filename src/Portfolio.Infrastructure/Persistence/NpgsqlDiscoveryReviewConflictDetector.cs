using Npgsql;
using Portfolio.Application.Common.Abstractions.Persistence;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlDiscoveryReviewConflictDetector : IDiscoveryReviewConflictDetector
{
    public bool IsReviewConflict(Exception exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "discovery_candidate_reviews_pkey" or "uq_discovery_review_event_version"
    };
}
