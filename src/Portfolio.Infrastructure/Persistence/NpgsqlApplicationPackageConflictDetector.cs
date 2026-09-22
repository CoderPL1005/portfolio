using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Persistence;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlApplicationPackageConflictDetector : IApplicationPackageConflictDetector
{
    private const string ManagedSnapshotConstraint = "uq_job_application_documents_managed_cv_revision";

    public bool IsManagedSnapshotConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ManagedSnapshotConstraint,
        };
}
