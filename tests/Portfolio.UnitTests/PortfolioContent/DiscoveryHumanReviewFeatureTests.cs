using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryHumanReviewFeatureTests
{
    private static ContentTestDbContext Db() => new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static SaveDiscoveryHumanReviewHandler Handler(ContentTestDbContext db, bool authenticated = true) =>
        new(db, new User(authenticated), TimeProvider.System, new NpgsqlDiscoveryReviewConflictDetector());
    private static async Task<Guid> Seed(ContentTestDbContext db)
    {
        await new ImportDiscoveryReviewHandler(db, TimeProvider.System).HandleAsync(new(DiscoveryReviewFeatureTests.Bundle()));
        return (await db.DiscoveryCandidates.SingleAsync()).Id;
    }
    [Fact] public async Task Review_history_versions_reimport_and_stale_retry_preserve_original_payload()
    {
        await using var db = Db(); var id = await Seed(db); var original = (await db.DiscoveryCandidates.SingleAsync()).PayloadHash;
        var h = Handler(db);
        var first = await h.HandleAsync(new(id, new(0, "CONFIRMED", null, null, "Xác nhận.")));
        Assert.Equal(1, first.Version); Assert.Equal("VALID_RECRUITMENT", first.ContentStatus);
        await Assert.ThrowsAsync<ConflictException>(() => h.HandleAsync(new(id, new(0, "UNCERTAIN", null, null, "Stale."))));
        var second = await h.HandleAsync(new(id, new(1, "CORRECTED", "INVALID_RECRUITMENT", "NOT_MATCHED", "Sửa content.")));
        Assert.Equal(2, second.Version);
        var third = await h.HandleAsync(new(id, new(2, "UNCERTAIN", null, null, "Chưa thể kết luận.")));
        Assert.Null(third.ContentStatus); Assert.Null(third.TargetStatus);
        Assert.Equal(3, (await db.DiscoveryCandidateReviewEvents.CountAsync()));
        var history = await new GetDiscoveryReviewHistoryHandler(db).HandleAsync(new(id, 2, 1));
        Assert.Equal(3, history.Total); Assert.Equal(2, Assert.Single(history.Items).Version);
        Assert.Equal(3, history.Current.Version);
        var repeat = await new ImportDiscoveryReviewHandler(db, TimeProvider.System).HandleAsync(new(DiscoveryReviewFeatureTests.Bundle()));
        Assert.Equal(1, repeat.Unchanged); Assert.Equal(original, (await db.DiscoveryCandidates.SingleAsync()).PayloadHash);
        Assert.Equal(3, await db.DiscoveryCandidateReviewEvents.CountAsync());
    }
    [Fact] public async Task No_actor_missing_candidate_and_identical_correction_never_write()
    {
        await using var db = Db(); var id = await Seed(db);
        await Assert.ThrowsAsync<UnauthorizedException>(() => Handler(db, false).HandleAsync(new(id, new(0,"CONFIRMED",null,null,"Reason"))));
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(db).HandleAsync(new(Guid.NewGuid(), new(0,"CONFIRMED",null,null,"Reason"))));
        await Assert.ThrowsAsync<ValidationException>(() => Handler(db).HandleAsync(new(id, new(0,"CORRECTED","VALID_RECRUITMENT","NOT_MATCHED","No change"))));
        Assert.Empty(await db.DiscoveryCandidateReviews.ToListAsync()); Assert.Empty(await db.DiscoveryCandidateReviewEvents.ToListAsync());
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Save_failure_detaches_pending_history_and_does_not_commit_review(bool concurrency, bool edit)
    {
        await using var db = Db(); var id = await Seed(db);
        if (edit) await Handler(db).HandleAsync(new(id,new(0,"CONFIRMED",null,null,"Initial")));
        db.ChangeTracker.Clear();
        db.FailSaveChanges = !concurrency; db.ConcurrencyFailuresRemaining = concurrency ? 1 : 0;
        var command = new SaveDiscoveryHumanReviewCommand(id,new(edit?1:0,"UNCERTAIN",null,null,"Reason"));
        if(concurrency) await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(command));
        else await Assert.ThrowsAsync<DbUpdateException>(() => Handler(db).HandleAsync(command));
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.Entity is Portfolio.Domain.Entities.DiscoveryCandidateReview or Portfolio.Domain.Entities.DiscoveryCandidateReviewEvent);
        Assert.Equal(edit?1:0,await db.DiscoveryCandidateReviews.CountAsync());
        Assert.Equal(edit?1:0,await db.DiscoveryCandidateReviewEvents.CountAsync());
        if(edit)Assert.Equal(1,(await db.DiscoveryCandidateReviews.AsNoTracking().SingleAsync()).Version);
    }
    [Fact] public void Only_known_Postgres_unique_constraints_are_review_conflicts()
    {
        var detector = new NpgsqlDiscoveryReviewConflictDetector();
        foreach(var name in new[]{"discovery_candidate_reviews_pkey","uq_discovery_review_event_version"})
            Assert.True(detector.IsReviewConflict(new DbUpdateException("safe", new PostgresException("safe","ERROR","ERROR","23505",constraintName:name))));
        Assert.False(detector.IsReviewConflict(new DbUpdateException("safe", new PostgresException("safe","ERROR","ERROR","23505",constraintName:"unrelated"))));
        Assert.False(detector.IsReviewConflict(new DbUpdateException("safe", new PostgresException("safe","ERROR","ERROR","23503",constraintName:"discovery_candidate_reviews_pkey"))));
    }
    [Theory]
    [InlineData(null,"CONFIRMED",null,null,"Reason")]
    [InlineData(-1,"CONFIRMED",null,null,"Reason")]
    [InlineData(int.MaxValue,"CONFIRMED",null,null,"Reason")]
    [InlineData(0,"confirmed",null,null,"Reason")]
    [InlineData(0,"UNREVIEWED",null,null,"Reason")]
    [InlineData(0,"CONFIRMED",null,null,"")]
    [InlineData(0,"UNCERTAIN",null,null,"  ")]
    [InlineData(0,"UNCERTAIN","NEEDS_REVIEW",null,"Reason")]
    [InlineData(0,"CONFIRMED",null,"NOT_MATCHED","Reason")]
    [InlineData(0,"CORRECTED",null,"NOT_MATCHED","Reason")]
    [InlineData(0,"CORRECTED","INVALID", "TARGET_MATCH","Reason")]
    [InlineData(0,"CONFIRMED",null,null,"NUL\0")]
    public void Invalid_reviews_are_rejected(int? version,string action,string? content,string? target,string reason) =>
        Assert.False(SaveDiscoveryHumanReviewValidator.Valid(new(Guid.NewGuid(),new(version,action,content,target,reason))));
    [Fact] public void Oversized_reason_is_rejected() => Assert.False(SaveDiscoveryHumanReviewValidator.Valid(new(Guid.NewGuid(),new(0,"CONFIRMED",null,null,new string('x',2001)))));

    private sealed class User(bool authenticated) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? AdminUserId => authenticated ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null;
        public string? Email => null;
    }
}
