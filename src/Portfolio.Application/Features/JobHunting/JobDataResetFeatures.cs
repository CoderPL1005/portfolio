using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record JobDataResetPlan(IReadOnlyDictionary<string, int> Counts, long TotalRecords, string Fingerprint);
public sealed record GetJobDataResetPlanQuery : IRequest<JobDataResetPlan>;
public sealed record ResetAllJobDataCommand(string Confirmation, string ExpectedFingerprint) : IRequest<JobDataResetResult>;
public sealed record JobDataResetResult(JobDataResetPlan Deleted, JobDataResetPlan Remaining);

public static class JobDataReset
{
    public const string Confirmation = "RESET ALL JOB DATA";
    public static async Task RequireInactiveExecutionAsync(IApplicationDbContext db,CancellationToken ct)
    {
        // UNKNOWN is not a closed attempt, even if a malformed row lost ActiveKey.
        // Production holds the execution table lock across this check and deletion.
        if(await db.DiscoveryExecutions.AnyAsync(e=>e.ActiveKey!=null ||
            (e.State!="SUCCEEDED"&&e.State!="PARTIAL"&&e.State!="FAILED"),ct))
            throw new ConflictException("JOB_DATA_RESET_ACTIVE_EXECUTION","An active or unresolved Discovery execution prevents reset. No data was deleted.");
    }
    public static async Task<JobDataResetPlan> CountsAsync(IApplicationDbContext db, CancellationToken ct)
    {
        await RequireInactiveExecutionAsync(db,ct);
        // Fixed scope. No Admin, Portfolio, CV, preferences, provider ledger or storage operations.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["discovery_candidates"] = await db.DiscoveryCandidates.CountAsync(ct),
            ["discovery_candidate_reviews"] = await db.DiscoveryCandidateReviews.CountAsync(ct),
            ["discovery_candidate_review_events"] = await db.DiscoveryCandidateReviewEvents.CountAsync(ct),
            ["discovery_execution_observations"] = await db.DiscoveryExecutionObservations.CountAsync(ct),
            ["discovery_execution_vacancies"] = await db.DiscoveryExecutionVacancies.CountAsync(ct),
            ["job_postings"] = await db.JobPostings.CountAsync(ct),
            ["raw_job_postings"] = await db.RawJobPostings.CountAsync(ct),
            ["raw_job_posting_attachments"] = await db.RawJobPostingAttachments.CountAsync(ct),
            ["job_applications"] = await db.JobApplications.CountAsync(ct),
            ["job_application_documents"] = await db.JobApplicationDocuments.CountAsync(ct),
            ["job_application_events"] = await db.JobApplicationEvents.CountAsync(ct),
            ["submission_attempts"] = await db.SubmissionAttempts.CountAsync(ct),
            ["submission_attempt_events"] = await db.SubmissionAttemptEvents.CountAsync(ct)
        };
        // Counts alone cannot detect replacing one row with another. Bind the
        // confirmation to the exact record set under the same transaction locks.
        Task<Guid[]> Ids<T>(DbSet<T> set,string key="Id") where T:class => set.AsNoTracking()
            .OrderBy(e=>EF.Property<Guid>(e,key)).Select(e=>EF.Property<Guid>(e,key)).ToArrayAsync(ct);
        var identities=new SortedDictionary<string,Guid[]>(StringComparer.Ordinal)
        {
            ["discovery_candidates"]=await Ids(db.DiscoveryCandidates),
            ["discovery_candidate_reviews"]=await Ids(db.DiscoveryCandidateReviews,"CandidateId"),
            ["discovery_candidate_review_events"]=await Ids(db.DiscoveryCandidateReviewEvents),
            ["discovery_execution_observations"]=await Ids(db.DiscoveryExecutionObservations),
            ["discovery_execution_vacancies"]=await Ids(db.DiscoveryExecutionVacancies),
            ["job_postings"]=await Ids(db.JobPostings),["raw_job_postings"]=await Ids(db.RawJobPostings),
            ["raw_job_posting_attachments"]=await Ids(db.RawJobPostingAttachments),
            ["job_applications"]=await Ids(db.JobApplications),["job_application_documents"]=await Ids(db.JobApplicationDocuments),
            ["job_application_events"]=await Ids(db.JobApplicationEvents),["submission_attempts"]=await Ids(db.SubmissionAttempts),
            ["submission_attempt_events"]=await Ids(db.SubmissionAttemptEvents)
        };
        return new(counts, counts.Values.Sum(n => (long)n), DiscoveryReviewRules.Hash(JsonSerializer.Serialize(new{counts,identities})));
    }

    public static async Task RecordResetAuditAsync(IApplicationDbContext db,Guid adminId,CancellationToken ct)
    {
        var observations=await db.DiscoveryExecutionObservations.AsNoTracking().OrderBy(o=>o.Id)
            .Select(o=>new{o.Id,o.ExecutionId,o.ItemIndex,o.RawJobPostingId,o.PayloadHash,o.ContentHash}).ToArrayAsync(ct);
        var vacancies=await db.DiscoveryExecutionVacancies.AsNoTracking().OrderBy(v=>v.Id)
            .Select(v=>new{v.Id,v.ObservationId,v.VacancyIndex,v.RawJobPostingId,v.JobPostingId,v.Fingerprint}).ToArrayAsync(ct);
        foreach(var group in observations.GroupBy(o=>o.ExecutionId))
        {
            var ids=group.Select(o=>o.Id).ToHashSet();
            // Explicitly retained provenance receipts, not raw text/model output.
            // Original execution/result hash, request claim and usage are unchanged.
            db.DiscoveryExecutionEvents.Add(new DiscoveryExecutionEvent{Id=Guid.NewGuid(),ExecutionId=group.Key,
                State="RECRUITMENT_DATA_RESET",At=DateTimeOffset.UtcNow,
                Reason=JsonSerializer.Serialize(new{adminId,observations=group.ToArray(),vacancies=vacancies.Where(v=>ids.Contains(v.ObservationId)).ToArray()})});
        }
        if(observations.Length>0)await db.SaveChangesAsync(ct);
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
            throw new ConflictException("JOB_DATA_RESET_STALE", "Previewed record set changed; refresh preview and confirm again.");
        await JobDataReset.RecordResetAuditAsync(db,user.AdminUserId!.Value,ct);
        await tx.DeleteAllJobDataAsync(ct);
        var after = await JobDataReset.CountsAsync(db, ct);
        if (after.TotalRecords != 0) throw new ConflictException("JOB_DATA_RESET_INCOMPLETE", "Reset incomplete; transaction will be rolled back.");
        await tx.CommitAsync(ct);
        return new(before, after);
    }
}
