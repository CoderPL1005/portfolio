using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IJobApplicationConflictDetector
{
    bool IsDuplicateJobPosting(DbUpdateException exception);
}
