using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscoveryReviewWrite(int? ExpectedVersion, string Action, string? ContentStatus, string? TargetStatus, string Notes);
public sealed record SaveDiscoveryHumanReviewCommand(Guid Id, DiscoveryReviewWrite Review) : IRequest<HumanReviewSummary>;
public sealed record GetDiscoveryReviewHistoryQuery(Guid Id, int Page = 1, int PageSize = 20) : IRequest<DiscoveryReviewHistory>;
public sealed record HumanReviewSummary(string Status, int Version, string? ContentStatus, string? TargetStatus,
    string? Notes, DateTimeOffset? ReviewedAt, Guid? ReviewedByAdminId)
{
    public static HumanReviewSummary Unreviewed => new("UNREVIEWED", 0, null, null, null, null, null);
    public static HumanReviewSummary From(DiscoveryCandidateReview? r) => r is null ? Unreviewed
        : new(r.Status, r.Version, r.ContentStatus, r.TargetStatus, r.Notes, r.ReviewedAt, r.ReviewedByAdminId);
}
public sealed record DiscoveryReviewHistory(IReadOnlyList<HumanReviewSummary> Items, int Page, int PageSize,
    int Total, int TotalPages, HumanReviewSummary Current);

public sealed class SaveDiscoveryHumanReviewValidator : IRequestValidator<SaveDiscoveryHumanReviewCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(SaveDiscoveryHumanReviewCommand r, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(Valid(r) ? [] : [new("review", "Invalid review action, version, labels or reason.")]);
    public static bool Valid(SaveDiscoveryHumanReviewCommand r)
    {
        var v = r.Review;
        return r.Id != Guid.Empty && v is not null && v.ExpectedVersion is >= 0 and < int.MaxValue
            && v.Notes is { Length: > 0 and <= 2000 } && !string.IsNullOrWhiteSpace(v.Notes) && !v.Notes.Contains('\0')
            && (v.Action is "CORRECTED" or "REVIEW_RECOVERY"
                ? DiscoveryReviewRules.ContentStatuses.Contains(v.ContentStatus) && DiscoveryReviewRules.TargetStatuses.Contains(v.TargetStatus)
                : v.Action is "CONFIRMED" or "UNCERTAIN" && v.ContentStatus is null && v.TargetStatus is null);
    }
}

public sealed class SaveDiscoveryHumanReviewHandler(IApplicationDbContext db, ICurrentUser user, TimeProvider clock,
    IDiscoveryReviewConflictDetector conflicts) : IRequestHandler<SaveDiscoveryHumanReviewCommand, HumanReviewSummary>
{
    public async Task<HumanReviewSummary> HandleAsync(SaveDiscoveryHumanReviewCommand r, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.AdminUserId is not { } actor || actor == Guid.Empty)
            throw new UnauthorizedException("UNAUTHORIZED", "An authenticated Admin is required.");
        if (!SaveDiscoveryHumanReviewValidator.Valid(r)) throw new ValidationException([new("review", "Invalid review.")]);
        var candidate = await db.DiscoveryCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.Id, ct)
            ?? throw new NotFoundException("DISCOVERY_CANDIDATE_NOT_FOUND", "Discovery observation not found.");
        var recovery=candidate.CandidateOrigin==DiscoveryRecoveryProvenance.Origin;
        if(recovery) DiscoveryRecoveryProvenance.Read(candidate);
        else if(candidate.CandidateOrigin!=DiscoveryRecoveryProvenance.Normal) throw DiscoveryRecoveryProvenance.Invalid();
        if(recovery && r.Review.Action is not("REVIEW_RECOVERY" or "UNCERTAIN") || !recovery && r.Review.Action=="REVIEW_RECOVERY")
            throw new ValidationException([new("review","This action does not apply to the observation provenance.")]);
        var review = await db.DiscoveryCandidateReviews.SingleOrDefaultAsync(x => x.CandidateId == r.Id, ct);
        if ((review?.Version ?? 0) != r.Review.ExpectedVersion) throw Stale();
        var v = r.Review;
        if (v.Action == "CORRECTED" && v.ContentStatus == candidate.ContentStatus && v.TargetStatus == candidate.TargetStatus)
            throw new ValidationException([new("review", "A correction must differ from the original prediction.")]);
        var creating = review is null;
        review ??= new DiscoveryCandidateReview { CandidateId = r.Id };
        review.Version = checked(review.Version + 1);
        review.Status = v.Action=="REVIEW_RECOVERY" ? "REVIEWED_RECOVERY" : v.Action;
        review.ContentStatus = v.Action == "CONFIRMED" ? candidate.ContentStatus : v.Action is "CORRECTED" or "REVIEW_RECOVERY" ? v.ContentStatus : null;
        review.TargetStatus = v.Action == "CONFIRMED" ? candidate.TargetStatus : v.Action is "CORRECTED" or "REVIEW_RECOVERY" ? v.TargetStatus : null;
        review.Notes = v.Notes.Trim(); review.ReviewedAt = clock.GetUtcNow(); review.ReviewedByAdminId = actor;
        if (creating) db.DiscoveryCandidateReviews.Add(review);
        var audit = new DiscoveryCandidateReviewEvent { Id = Guid.NewGuid(), CandidateId = r.Id, Version = review.Version,
            Status = review.Status, ContentStatus = review.ContentStatus, TargetStatus = review.TargetStatus,
            Notes = review.Notes, ReviewedAt = review.ReviewedAt, ReviewedByAdminId = actor, ObservationPayloadHash = candidate.PayloadHash };
        db.DiscoveryCandidateReviewEvents.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { Detach(); throw Stale(); }
        catch (DbUpdateException e) when (conflicts.IsReviewConflict(e)) { Detach(); throw Stale(); }
        catch { Detach(); throw; }
        return HumanReviewSummary.From(review);

        void Detach()
        {
            db.DiscoveryCandidateReviewEvents.Entry(audit).State = EntityState.Detached;
            db.DiscoveryCandidateReviews.Entry(review).State = EntityState.Detached;
        }
    }
    private static ConflictException Stale() => new("DISCOVERY_REVIEW_VERSION_CONFLICT", "Review changed; reload before deciding again.");
}

public sealed class GetDiscoveryReviewHistoryValidator : IRequestValidator<GetDiscoveryReviewHistoryQuery>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetDiscoveryReviewHistoryQuery r, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(r.Id != Guid.Empty && r.Page is >= 1 and <= 100000 && r.PageSize is >= 1 and <= 100
            ? [] : [new("page", "Invalid review history pagination.")]);
}
public sealed class GetDiscoveryReviewHistoryHandler(IApplicationDbContext db) : IRequestHandler<GetDiscoveryReviewHistoryQuery, DiscoveryReviewHistory>
{
    public async Task<DiscoveryReviewHistory> HandleAsync(GetDiscoveryReviewHistoryQuery r, CancellationToken ct = default)
    {
        if (!await db.DiscoveryCandidates.AnyAsync(x => x.Id == r.Id, ct))
            throw new NotFoundException("DISCOVERY_CANDIDATE_NOT_FOUND", "Discovery observation not found.");
        var q = db.DiscoveryCandidateReviewEvents.AsNoTracking().Where(x => x.CandidateId == r.Id);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Version).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize)
            .Select(x => new HumanReviewSummary(x.Status, x.Version, x.ContentStatus, x.TargetStatus, x.Notes, x.ReviewedAt, x.ReviewedByAdminId)).ToArrayAsync(ct);
        return new(rows, r.Page, r.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)r.PageSize),
            HumanReviewSummary.From(await db.DiscoveryCandidateReviews.AsNoTracking().SingleOrDefaultAsync(x => x.CandidateId == r.Id, ct)));
    }
}
