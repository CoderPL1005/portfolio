using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryReviewFeatureTests
{
    public static DiscoveryReviewImport Bundle()
    {
        const string text = "Hiring Senior Java Developer in Hanoi";
        var item = new DiscoveryReviewItem(0, "123", "https://www.facebook.com/fixture/posts/pfbidSynthetic", text,
            DiscoveryReviewRules.Hash(text), DiscoveryReviewRules.Hash(text), null,
            new("VALID_RECRUITMENT", ["EMPLOYER_HIRING"], "Có cơ hội tuyển dụng, chưa xác minh công ty.", [text]),
            new("NOT_MATCHED", ["SENIORITY_MISMATCH"], "Không phù hợp mục tiêu.", []), "NOT_CHECKED",
            JsonSerializer.SerializeToElement(new { company = (string?)null, technologies = new[] { "Java" }, vacancies = new object[0] }),
            ["COMPANY", "PUBLISHED_AT"], ["POST_ID_URL_MISMATCH"], "content-first-v1", DateTimeOffset.Parse("2026-10-26T03:00:00Z"));
        return new(1, "SOCIALAPIS", "synthetic-unit", "Backend Hanoi", 1, [item]);
    }
    private static ContentTestDbContext Db() => new(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact] public async Task Import_is_immutable_idempotent_and_never_promotes_jobs()
    {
        await using var db = Db(); var b = Bundle();
        Assert.True(DiscoveryReviewRules.Valid(b));
        var handler = new ImportDiscoveryReviewHandler(db, TimeProvider.System);
        Assert.Equal(new DiscoveryImportResult(1, 0), await handler.HandleAsync(new(b)));
        Assert.Equal(new DiscoveryImportResult(0, 1), await handler.HandleAsync(new(b)));
        Assert.Single(await db.DiscoveryCandidates.ToListAsync());
        Assert.Empty(await db.JobPostings.ToListAsync()); Assert.Empty(await db.RawJobPostings.ToListAsync());
        Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        var altered = b with { Items = [b.Items[0] with { ProviderPostId = "124" }] };
        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new(altered)));
        Assert.Equal("123", DiscoveryReviewRules.Detail(await db.DiscoveryCandidates.SingleAsync(), 1).Item.ProviderPostId);
    }
    [Fact] public async Task Page_coordinates_and_reposts_keep_all_observations_and_filter_counts()
    {
        await using var db = Db(); var h = new ImportDiscoveryReviewHandler(db, TimeProvider.System); var b = Bundle();
        await h.HandleAsync(new(b)); await h.HandleAsync(new(b with { RunId = "another-run" })); await h.HandleAsync(new(b with { PageNumber = 2 }));
        var result = await new GetDiscoveryReviewHandler(db).HandleAsync(new(TargetStatus: "NOT_MATCHED", DuplicatesOnly: true));
        Assert.Equal(3, result.Counts.Total); Assert.Equal(3, result.Counts.Valid); Assert.Equal(3, result.Counts.DuplicateItems);
        Assert.All(result.Results.Items, d => { Assert.Equal(3, d.DuplicateCount); Assert.Equal("NOT_CHECKED", d.Item.VerificationStatus); });
        Assert.Equal(2, (await new GetDiscoveryReviewHandler(db).HandleAsync(new(RunId: b.RunId))).Counts.Total);
    }
    [Fact] public async Task Missing_candidate_is_not_found()
    {
        await using var db = Db();
        await Assert.ThrowsAsync<NotFoundException>(() => new GetDiscoveryCandidateHandler(db).HandleAsync(new(Guid.NewGuid())));
    }
    [Fact] public async Task Database_failure_does_not_leave_tracked_imports_or_promote_jobs()
    {
        await using var db = Db(); db.FailSaveChanges = true;
        await Assert.ThrowsAsync<ConflictException>(() => new ImportDiscoveryReviewHandler(db, TimeProvider.System).HandleAsync(new(Bundle())));
        Assert.Empty(db.ChangeTracker.Entries<Portfolio.Domain.Entities.DiscoveryCandidate>());
        Assert.Empty(await db.DiscoveryCandidates.ToListAsync()); Assert.Empty(await db.JobPostings.ToListAsync());
    }
    [Fact] public void Deep_extraction_returns_validation_failure_instead_of_throwing()
    {
        var text = "null"; for (var n = 0; n < 30; n++) text = "{\"company\":" + text + "}";
        var b = Bundle();
        Assert.False(DiscoveryReviewRules.Valid(b with { Items = [b.Items[0] with { Extracted = JsonDocument.Parse(text).RootElement.Clone() }] }));
    }
    [Theory]
    [InlineData("verified")][InlineData("hash")][InlineData("url")][InlineData("snippet")][InlineData("extracted")][InlineData("index")][InlineData("oversized")][InlineData("missing-decision")]
    public void Unsafe_or_forged_import_is_rejected(string problem)
    {
        var b = Bundle(); var i = b.Items[0];
        i = problem switch
        {
            "verified" => i with { VerificationStatus = "VERIFIED" },
            "hash" => i with { ContentHash = new string('0', 64) },
            "url" => i with { SourceUrl = "javascript:alert(1)" },
            "snippet" => i with { ContentValidity = i.ContentValidity with { EvidenceSnippets = ["invented"] } },
            "extracted" => i with { Extracted = JsonSerializer.SerializeToElement(new { storageKey = "private" }) },
            "index" => i with { ItemIndex = 5 },
            "oversized" => i with { RawContent = new string('x', 24001) },
            _ => i with { ContentValidity = null! }
        };
        Assert.False(DiscoveryReviewRules.Valid(b with { Items = [i] }));
    }
}
