using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IApplicationPackageConflictDetector
{
    bool IsManagedSnapshotConflict(DbUpdateException exception);
}
