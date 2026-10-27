using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.JobHunting;

// This endpoint remains read-only; deletion uses the separate backed-up cleanup command.
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
        var snapshot = await DiscoveryCleanup.ReadAsync(db, scope, ct);
        var fingerprint = DiscoveryCleanup.Hash(snapshot);
        return new(true, scope, await db.JobPostings.CountAsync(ct), await db.DiscoveryCandidates.CountAsync(ct),
            snapshot.Candidates.Length, snapshot.Reviews.Length, snapshot.ReviewEvents.Length, fingerprint,
            snapshot.Candidates.Length == 0 ? "SKIP_DELETE_EMPTY_SCOPE" : "BACKUP_AND_RECONFIRM_EXACT_SCOPE_BEFORE_DELETE", true);
    }
}
