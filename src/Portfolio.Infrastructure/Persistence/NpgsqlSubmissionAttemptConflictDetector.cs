using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Persistence;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlSubmissionAttemptConflictDetector : ISubmissionAttemptConflictDetector
{
    public bool IsDuplicateIdempotencyKey(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "uq_submission_attempts_idempotency_key",
    };

    public bool IsNonRetryableAttemptConflict(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "uq_submission_attempts_non_retryable_context",
    };
}
