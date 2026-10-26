using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryDemoResetPlanTests
{
    private static ContentTestDbContext Db() => new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Empty_scope_is_idempotent_read_only_skip_not_an_assumption_about_production()
    {
        await using var db = Db(); var handler = new PlanDiscoveryDemoResetHandler(db);
        var request = new PlanDiscoveryDemoResetQuery(["old-demo"]);
        var first = await handler.HandleAsync(request); var second = await handler.HandleAsync(request);
        Assert.Equal(first.Fingerprint, second.Fingerprint); Assert.True(first.DryRun);
        Assert.Equal("SKIP_DELETE_EMPTY_SCOPE", first.Action); Assert.True(first.BackupAndSeparateConfirmationRequired);
        Assert.Equal(0, first.ScopedObservations); Assert.Equal(0, first.TotalJobPostings);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Explicit_scope_counts_observations_and_human_history_without_deleting_or_exposing_private_payload()
    {
        await using var db = Db(); var import = new ImportDiscoveryReviewHandler(db, TimeProvider.System);
        var bundle = DiscoveryReviewFeatureTests.Bundle();
        await import.HandleAsync(new(bundle with { RunId = "old-demo" }));
        await import.HandleAsync(new(bundle with { RunId = "keep-me" }));
        var candidate = await db.DiscoveryCandidates.SingleAsync(c => c.RunId == "old-demo"); var id = candidate.Id;
        db.DiscoveryCandidateReviews.Add(new() { CandidateId = id, Version = 1, Status = "UNCERTAIN", Notes = "private note" });
        db.DiscoveryCandidateReviewEvents.Add(new() { Id = Guid.NewGuid(), CandidateId = id, Version = 1, Status = "UNCERTAIN", Notes = "private note", ObservationPayloadHash = candidate.PayloadHash });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var handler = new PlanDiscoveryDemoResetHandler(db); var request = new PlanDiscoveryDemoResetQuery(["old-demo"]);
        var first = await handler.HandleAsync(request);
        Assert.Equal(2, first.TotalObservations); Assert.Equal(1, first.ScopedObservations);
        Assert.Equal(1, first.ScopedReviews); Assert.Equal(1, first.ScopedReviewEvents);
        Assert.Equal("BACKUP_AND_RECONFIRM_EXACT_SCOPE_BEFORE_DELETE", first.Action);
        Assert.Equal(2, await db.DiscoveryCandidates.CountAsync()); Assert.Single(await db.DiscoveryCandidateReviewEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await db.JobPostings.ToListAsync()); Assert.Empty(await db.RawJobPostings.ToListAsync());
        Assert.Empty(await db.JobApplications.ToListAsync()); Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.DoesNotContain("private note", System.Text.Json.JsonSerializer.Serialize(first));
        var review = await db.DiscoveryCandidateReviews.SingleAsync(); review.Version++; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.NotEqual(first.Fingerprint, (await handler.HandleAsync(request)).Fingerprint);
    }

    [Theory]
    [InlineData("")][InlineData("*")][InlineData("old/../../")][InlineData("old\n")]
    public async Task Broad_or_malformed_scope_is_rejected(string value)
    {
        await using var db = Db();
        await Assert.ThrowsAsync<ValidationException>(() => new PlanDiscoveryDemoResetHandler(db).HandleAsync(new([value])));
        Assert.False(PlanDiscoveryDemoResetValidator.Valid(new(["same", "same"])));
        Assert.False(PlanDiscoveryDemoResetValidator.Valid(new([])));
    }
}
