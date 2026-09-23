using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobApplicationConflictDetectorTests
{
    [Fact]
    public void Detector_recognizes_only_the_job_application_unique_index()
    {
        var detector = new NpgsqlJobApplicationConflictDetector();

        Assert.True(detector.IsDuplicateJobPosting(Failure("ix_job_applications_job_posting_id")));
        Assert.False(detector.IsDuplicateJobPosting(Failure("another_unique_constraint")));
        Assert.False(detector.IsDuplicateJobPosting(new DbUpdateException("write failed")));
    }

    [Fact]
    public void Submission_detector_recognizes_only_the_idempotency_unique_index()
    {
        var detector = new NpgsqlSubmissionAttemptConflictDetector();

        Assert.True(detector.IsDuplicateIdempotencyKey(Failure("uq_submission_attempts_idempotency_key")));
        Assert.False(detector.IsDuplicateIdempotencyKey(Failure("another_unique_constraint")));
        Assert.False(detector.IsDuplicateIdempotencyKey(new DbUpdateException("write failed")));
        Assert.True(detector.IsNonRetryableAttemptConflict(Failure("uq_submission_attempts_non_retryable_context")));
        Assert.False(detector.IsNonRetryableAttemptConflict(Failure("uq_submission_attempts_idempotency_key")));
    }

    private static DbUpdateException Failure(string constraint) => new(
        "write failed",
        new PostgresException("write failed", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: constraint));
}
