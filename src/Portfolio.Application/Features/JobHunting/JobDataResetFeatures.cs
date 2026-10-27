using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.JobHunting;

public sealed record JobDataResetPlan(IReadOnlyDictionary<string, int> Counts, long TotalRecords, string Fingerprint);
public sealed record GetJobDataResetPlanQuery : IRequest<JobDataResetPlan>;
public sealed record ResetAllJobDataCommand(string Confirmation, string ExpectedFingerprint) : IRequest<JobDataResetResult>;
public sealed record JobDataResetResult(JobDataResetPlan Deleted, JobDataResetPlan Remaining);

public static class JobDataReset
{
    public const string Confirmation = "RESET ALL JOB DATA";
    public static async Task<JobDataResetPlan> CountsAsync(IApplicationDbContext db, CancellationToken ct)
    {
        // Fixed scope. No Admin, Portfolio, CV, preferences, provider ledger or storage operations.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["discovery_candidates"] = await db.DiscoveryCandidates.CountAsync(ct),
            ["discovery_candidate_reviews"] = await db.DiscoveryCandidateReviews.CountAsync(ct),
            ["discovery_candidate_review_events"] = await db.DiscoveryCandidateReviewEvents.CountAsync(ct),
            ["job_postings"] = await db.JobPostings.CountAsync(ct),
            ["raw_job_postings"] = await db.RawJobPostings.CountAsync(ct),
            ["raw_job_posting_attachments"] = await db.RawJobPostingAttachments.CountAsync(ct),
            ["job_applications"] = await db.JobApplications.CountAsync(ct),
            ["job_application_documents"] = await db.JobApplicationDocuments.CountAsync(ct),
            ["job_application_events"] = await db.JobApplicationEvents.CountAsync(ct),
            ["submission_attempts"] = await db.SubmissionAttempts.CountAsync(ct),
            ["submission_attempt_events"] = await db.SubmissionAttemptEvents.CountAsync(ct)
        };
        return new(counts, counts.Values.Sum(n => (long)n), DiscoveryReviewRules.Hash(JsonSerializer.Serialize(counts)));
    }
}
public sealed class GetJobDataResetPlanHandler(IApplicationDbContext db, ICurrentUser user, IDiscoveryCleanupTransactionFactory transactions)
    : IRequestHandler<GetJobDataResetPlanQuery, JobDataResetPlan>
{
    public async Task<JobDataResetPlan> HandleAsync(GetJobDataResetPlanQuery r, CancellationToken ct = default)
    {
        DiscoveryCleanup.RequireAdmin(user);
        await using var tx = await transactions.BeginResetAllAsync(ct);
        var plan = await JobDataReset.CountsAsync(db, ct);
        await tx.CommitAsync(ct);
        return plan;
    }
}
public sealed class ResetAllJobDataHandler(IApplicationDbContext db, ICurrentUser user, IDiscoveryCleanupTransactionFactory transactions)
    : IRequestHandler<ResetAllJobDataCommand, JobDataResetResult>
{
    public async Task<JobDataResetResult> HandleAsync(ResetAllJobDataCommand r, CancellationToken ct = default)
    {
        DiscoveryCleanup.RequireAdmin(user);
        if (r.Confirmation != JobDataReset.Confirmation || string.IsNullOrEmpty(r.ExpectedFingerprint))
            throw new ValidationException([new("confirmation", "Preview and type RESET ALL JOB DATA before deletion.")]);
        await using var tx = await transactions.BeginResetAllAsync(ct);
        var before = await JobDataReset.CountsAsync(db, ct);
        if (r.ExpectedFingerprint != before.Fingerprint)
            throw new ConflictException("JOB_DATA_RESET_STALE", "Record counts changed; refresh preview and confirm again.");
        await tx.DeleteAllJobDataAsync(ct);
        var after = await JobDataReset.CountsAsync(db, ct);
        if (after.TotalRecords != 0) throw new ConflictException("JOB_DATA_RESET_INCOMPLETE", "Reset incomplete; transaction will be rolled back.");
        await tx.CommitAsync(ct);
        return new(before, after);
    }
}
