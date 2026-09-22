using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface ICanonicalCvConflictDetector
{
    bool IsSingletonConflict(DbUpdateException exception);
}
