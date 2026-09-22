using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Persistence;

namespace Portfolio.Infrastructure.Persistence;

public sealed class NpgsqlCanonicalCvConflictDetector : ICanonicalCvConflictDetector
{
    public bool IsSingletonConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_canonical_cvs_singleton",
        };
}
