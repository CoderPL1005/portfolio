using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record DiscoveryRunSummary(string RunId, int Observations, bool Protected);
public sealed record GetDiscoveryCleanupRunsQuery : IRequest<IReadOnlyList<DiscoveryRunSummary>>;
public sealed record DiscoveryCleanupSnapshot(int SchemaVersion, string[] RunIds, DiscoveryCandidate[] Candidates,
    DiscoveryCandidateReview[] Reviews, DiscoveryCandidateReviewEvent[] ReviewEvents);
public sealed record DiscoveryCleanupBackup(DiscoveryCleanupSnapshot Snapshot, string Fingerprint);
public sealed record ExportDiscoveryCleanupBackupCommand(string[] RunIds) : IRequest<DiscoveryCleanupBackup>;
public sealed record DeleteDiscoveryRunsCommand(string[] RunIds, string ExpectedFingerprint, DiscoveryCleanupBackup Backup,
    bool BackupSaved, string Confirmation) : IRequest<DiscoveryCleanupResult>;
public sealed record DiscoveryCleanupResult(int DeletedObservations, int DeletedReviews, int DeletedReviewEvents);

public static class DiscoveryCleanup
{
    public const string Confirmation = "DELETE LEGACY DISCOVERY";
    public const int MaximumBackupBytes = 4 * 1024 * 1024;
    public static void ValidateScope(string[] ids)
    {
        if (!PlanDiscoveryDemoResetValidator.Valid(new(ids))) throw new ValidationException([new("runIds", "Explicit run IDs required for scoped cleanup.")]);
    }
    public static string Hash(DiscoveryCleanupSnapshot snapshot) => DiscoveryReviewRules.Hash(JsonSerializer.Serialize(snapshot, DiscoveryReviewRules.Json));
    public static void RequireAdmin(ICurrentUser user)
    {
        if (!user.IsAuthenticated || user.AdminUserId is not { } id || id == Guid.Empty)
            throw new UnauthorizedException("UNAUTHORIZED", "Admin authentication required.");
    }
    public static async Task<DiscoveryCleanupSnapshot> ReadAsync(IApplicationDbContext db, string[] ids, CancellationToken ct)
    {
        ValidateScope(ids);
        var candidates = await db.DiscoveryCandidates.AsNoTracking().Where(c => ids.Contains(c.RunId)).Take(2001).ToArrayAsync(ct);
        if (candidates.Length > 2000) throw new ValidationException([new("runIds", "Cleanup scope exceeds 2000 observations; select fewer runs.")]);
        var keys = candidates.Select(c => c.Id).ToArray();
        var reviews = await db.DiscoveryCandidateReviews.AsNoTracking().Where(c => keys.Contains(c.CandidateId)).ToArrayAsync(ct);
        var events = await db.DiscoveryCandidateReviewEvents.AsNoTracking().Where(c => keys.Contains(c.CandidateId)).Take(10001).ToArrayAsync(ct);
        if (events.Length > 10000) throw new ValidationException([new("runIds", "Cleanup scope exceeds 10000 review events; select fewer runs.")]);
        var snapshot = new DiscoveryCleanupSnapshot(1, ids.Order(StringComparer.Ordinal).ToArray(), candidates.OrderBy(c => c.Id).ToArray(),
            reviews.OrderBy(c => c.CandidateId).ToArray(), events.OrderBy(c => c.Id).ToArray());
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, DiscoveryReviewRules.Json).Length > MaximumBackupBytes)
            throw new ValidationException([new("runIds", "Backup too large; select fewer runs.")]);
        return snapshot;
    }
}

public sealed class GetDiscoveryCleanupRunsHandler(IApplicationDbContext db) : IRequestHandler<GetDiscoveryCleanupRunsQuery, IReadOnlyList<DiscoveryRunSummary>>
{
    public async Task<IReadOnlyList<DiscoveryRunSummary>> HandleAsync(GetDiscoveryCleanupRunsQuery r, CancellationToken ct = default) =>
        await db.DiscoveryCandidates.AsNoTracking().GroupBy(c => c.RunId).OrderBy(g => g.Key)
            .Select(g => new DiscoveryRunSummary(g.Key, g.Count(), false)).ToListAsync(ct);
}
public sealed class ExportDiscoveryCleanupBackupHandler(IApplicationDbContext db, ICurrentUser user, IDiscoveryCleanupTransactionFactory transactions)
    : IRequestHandler<ExportDiscoveryCleanupBackupCommand, DiscoveryCleanupBackup>
{
    public async Task<DiscoveryCleanupBackup> HandleAsync(ExportDiscoveryCleanupBackupCommand r, CancellationToken ct = default)
    {
        DiscoveryCleanup.RequireAdmin(user); DiscoveryCleanup.ValidateScope(r.RunIds);
        await using var tx = await transactions.BeginAsync(ct);
        var snapshot = await DiscoveryCleanup.ReadAsync(db, r.RunIds, ct);
        await tx.CommitAsync(ct);
        return new(snapshot, DiscoveryCleanup.Hash(snapshot));
    }
}
public sealed class DeleteDiscoveryRunsHandler(IApplicationDbContext db, ICurrentUser user, IDiscoveryCleanupTransactionFactory transactions)
    : IRequestHandler<DeleteDiscoveryRunsCommand, DiscoveryCleanupResult>
{
    public async Task<DiscoveryCleanupResult> HandleAsync(DeleteDiscoveryRunsCommand r, CancellationToken ct = default)
    {
        DiscoveryCleanup.RequireAdmin(user); DiscoveryCleanup.ValidateScope(r.RunIds);
        if (!r.BackupSaved || r.Confirmation != DiscoveryCleanup.Confirmation || r.Backup?.Snapshot is not { } backup ||
            backup.SchemaVersion != 1 || backup.RunIds is null || !backup.RunIds.SequenceEqual(r.RunIds.Order(StringComparer.Ordinal)) ||
            r.ExpectedFingerprint != r.Backup.Fingerprint || DiscoveryCleanup.Hash(backup) != r.ExpectedFingerprint)
            throw new ValidationException([new("backup", "Downloaded complete backup and separate exact confirmation required.")]);
        await using var tx = await transactions.BeginAsync(ct);
        var snapshot = await DiscoveryCleanup.ReadAsync(db, r.RunIds, ct);
        if (DiscoveryCleanup.Hash(snapshot) != r.ExpectedFingerprint)
            throw new ConflictException("DISCOVERY_CLEANUP_STALE", "Discovery data changed. Preview and download a new backup; do not retry automatically.");
        // RESTRICT foreign keys: children first, all inside this transaction.
        db.DiscoveryCandidateReviewEvents.RemoveRange(snapshot.ReviewEvents);
        db.DiscoveryCandidateReviews.RemoveRange(snapshot.Reviews);
        db.DiscoveryCandidates.RemoveRange(snapshot.Candidates);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(snapshot.Candidates.Length, snapshot.Reviews.Length, snapshot.ReviewEvents.Length);
    }
}
