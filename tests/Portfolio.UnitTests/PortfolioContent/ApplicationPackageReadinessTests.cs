using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ApplicationPackageReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ready_requires_draft_approved_active_job_and_structurally_valid_cv()
    {
        var result = new ApplicationPackageReadinessEvaluator().Evaluate(Application(), Job(), Cv());

        Assert.True(result.IsReady);
        Assert.Equal("READY_TO_FINALIZE", result.Status);
        Assert.Empty(result.Blockers);
        Assert.Equal(3, result.ApplicationVersion);
        Assert.Equal(5, result.JobPostingVersion);
        Assert.Equal(2, result.CanonicalCvVersion);
        Assert.Equal("canonical.pdf", result.CanonicalCv!.FileName);
        Assert.DoesNotContain(typeof(ApplicationPackageCanonicalCv).GetProperties(), property => property.Name == "StorageKey");
    }

    [Fact]
    public void Not_ready_returns_all_applicable_blockers_in_deterministic_order()
    {
        var application = Application();
        application.Status = "APPLIED";
        var job = Job();
        job.SelectionStatus = "SKIPPED";
        job.ArchivedAt = Now;
        var cv = Cv();
        cv.ContentHash = new string('A', 64);

        var result = new ApplicationPackageReadinessEvaluator().Evaluate(application, job, cv);

        Assert.False(result.IsReady);
        Assert.Equal("NOT_READY", result.Status);
        Assert.Equal(
            ["APPLICATION_NOT_DRAFT", "JOB_NOT_APPROVED", "JOB_ARCHIVED", "CANONICAL_CV_INVALID"],
            result.Blockers.Select(blocker => blocker.Code));
    }

    [Fact]
    public void Missing_job_and_cv_are_reported_without_inventing_other_job_blockers()
    {
        var result = new ApplicationPackageReadinessEvaluator().Evaluate(Application(), null, null);

        Assert.Equal(["JOB_NOT_FOUND", "CANONICAL_CV_MISSING"], result.Blockers.Select(blocker => blocker.Code));
        Assert.Null(result.JobPostingVersion);
        Assert.Null(result.CanonicalCvVersion);
        Assert.Null(result.CanonicalCv);
    }

    [Theory]
    [InlineData("storage", "canonical.pdf", "application/pdf", 1, 1, true)]
    [InlineData("", "canonical.pdf", "application/pdf", 1, 1, false)]
    [InlineData("storage", "", "application/pdf", 1, 1, false)]
    [InlineData("storage", "canonical.pdf", "Application/Pdf", 1, 1, false)]
    [InlineData("storage", "canonical.pdf", "application/pdf", 0, 1, false)]
    [InlineData("storage", "canonical.pdf", "application/pdf", 10485761, 1, false)]
    [InlineData("storage", "canonical.pdf", "application/pdf", 1, 0, false)]
    public void Cv_metadata_validation_is_structural_and_exact(string storageKey, string fileName, string contentType, long size, int version, bool expectedReady)
    {
        var cv = Cv();
        cv.StorageKey = storageKey;
        cv.FileName = fileName;
        cv.ContentType = contentType;
        cv.FileSizeBytes = size;
        cv.Version = version;

        var result = new ApplicationPackageReadinessEvaluator().Evaluate(Application(), Job(), cv);

        Assert.Equal(expectedReady, result.IsReady);
        Assert.Equal(!expectedReady, result.Blockers.Any(blocker => blocker.Code == "CANONICAL_CV_INVALID"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Cv_hash_must_be_exactly_lowercase_sha256(string hash)
    {
        var cv = Cv();
        cv.ContentHash = hash;

        var result = new ApplicationPackageReadinessEvaluator().Evaluate(Application(), Job(), cv);

        Assert.Contains(result.Blockers, blocker => blocker.Code == "CANONICAL_CV_INVALID");
    }

    [Fact]
    public void Readiness_handler_has_no_storage_or_ai_dependency()
    {
        var dependencies = typeof(GetApplicationPackageReadinessQueryHandler).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType.Namespace?.Contains(".AI", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Handler_reads_current_versions_without_mutating_entities()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var application = Application();
        var job = Job(application.JobPostingId);
        var cv = Cv();
        db.AddRange(job, application, cv);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetApplicationPackageReadinessQueryHandler(db, new()).HandleAsync(new(application.Id));

        Assert.True(result.IsReady);
        Assert.Equal(3, result.ApplicationVersion);
        Assert.Equal(5, result.JobPostingVersion);
        Assert.Equal(2, result.CanonicalCvVersion);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Handler_returns_normal_not_found_for_missing_application()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetApplicationPackageReadinessQueryHandler(db, new()).HandleAsync(new(Guid.NewGuid())));
        Assert.Equal("JOB_APPLICATION_NOT_FOUND", error.Code);
    }

    private static JobApplication Application() => new()
    {
        Id = Guid.NewGuid(),
        JobPostingId = Guid.NewGuid(),
        Status = "DRAFT",
        Version = 3,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static JobPosting Job(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CompanyName = "Acme",
        PositionTitle = "Developer",
        Location = "Hanoi",
        Description = "Description",
        TechnologyStack = JsonDocument.Parse("[]"),
        VerificationStatus = "VERIFIED",
        SelectionStatus = "APPROVED",
        Version = 5,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static CanonicalCv Cv() => new()
    {
        Id = Guid.NewGuid(),
        SingletonKey = 1,
        StorageKey = "canonical-cv/private.pdf",
        FileName = "canonical.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 1024,
        ContentHash = new string('a', 64),
        Version = 2,
        CreatedAt = Now,
        UpdatedAt = Now
    };
}
