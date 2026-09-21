using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Persistence;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlJobApplicationConflictDetector : IJobApplicationConflictDetector
{
    public bool IsDuplicateJobPosting(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_job_applications_job_posting_id",
        };
}
