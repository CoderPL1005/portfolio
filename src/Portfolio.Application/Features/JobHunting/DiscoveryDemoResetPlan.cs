using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.JobHunting;

// Read-only planning only. No endpoint in this feature executes deletion.
public sealed record PlanDiscoveryDemoResetQuery(string[] RunIds) : IRequest<DiscoveryDemoResetPlan>;
public sealed record DiscoveryDemoResetPlan(bool DryRun, string[] RunIds, int TotalJobPostings, int TotalObservations,
    int ScopedObservations, int ScopedReviews, int ScopedReviewEvents, string Fingerprint, string Action,
    bool BackupAndSeparateConfirmationRequired);

public sealed class PlanDiscoveryDemoResetValidator : IRequestValidator<PlanDiscoveryDemoResetQuery>
{
    public static bool Valid(PlanDiscoveryDemoResetQuery r) => r.RunIds is { Length: > 0 and <= 10 }
        && r.RunIds.All(id => id is not null && Regex.IsMatch(id, "\\A[A-Za-z0-9._-]{1,100}\\z"))
        && r.RunIds.Distinct(StringComparer.Ordinal).Count() == r.RunIds.Length;
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(PlanDiscoveryDemoResetQuery r, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(Valid(r) ? [] : [new("runIds", "Explicit unique discovery run IDs are required.")]);
}

public sealed class PlanDiscoveryDemoResetHandler(IApplicationDbContext db) : IRequestHandler<PlanDiscoveryDemoResetQuery, DiscoveryDemoResetPlan>
{
    public async Task<DiscoveryDemoResetPlan> HandleAsync(PlanDiscoveryDemoResetQuery r, CancellationToken ct = default)
    {
        if (!PlanDiscoveryDemoResetValidator.Valid(r)) throw new ValidationException([new("runIds", "Invalid discovery reset scope.")]);
        var scope = r.RunIds.Order(StringComparer.Ordinal).ToArray();
        var rows = await db.DiscoveryCandidates.AsNoTracking().Where(c => scope.Contains(c.RunId)).OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.RunId, c.PayloadHash }).ToListAsync(ct);
        var ids = rows.Select(c => c.Id).ToArray();
        var reviews = await db.DiscoveryCandidateReviews.AsNoTracking().Where(c => ids.Contains(c.CandidateId)).OrderBy(c => c.CandidateId)
            .Select(c => new { c.CandidateId, c.Version }).ToListAsync(ct);
        var events = await db.DiscoveryCandidateReviewEvents.AsNoTracking().Where(c => ids.Contains(c.CandidateId)).OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.CandidateId, c.Version }).ToListAsync(ct);
        var fingerprint = DiscoveryReviewRules.Hash(JsonSerializer.Serialize(new { scope, rows, reviews, events }, DiscoveryReviewRules.Json));
        return new(true, scope, await db.JobPostings.CountAsync(ct), await db.DiscoveryCandidates.CountAsync(ct),
            rows.Count, reviews.Count, events.Count, fingerprint,
            rows.Count == 0 ? "SKIP_DELETE_EMPTY_SCOPE" : "BACKUP_AND_RECONFIRM_EXACT_SCOPE_BEFORE_DELETE", true);
    }
}
