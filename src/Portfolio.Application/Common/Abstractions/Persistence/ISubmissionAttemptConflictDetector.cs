using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface ISubmissionAttemptConflictDetector
{
    bool IsDuplicateIdempotencyKey(DbUpdateException exception);
    bool IsNonRetryableAttemptConflict(DbUpdateException exception);
}
