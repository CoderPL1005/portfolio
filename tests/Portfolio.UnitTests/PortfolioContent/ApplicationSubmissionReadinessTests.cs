using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ApplicationSubmissionReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Finalized_valid_package_is_ready_and_returns_safe_handoff()
    {
        var application = FinalizedApplication();
        var result = new ApplicationSubmissionReadinessEvaluator().Evaluate(application, Snapshot(application.Id));

        Assert.True(result.IsReady);
        Assert.Equal("READY_FOR_SUBMISSION", result.Status);
        Assert.Empty(result.Blockers);
        Assert.Equal((application.Id, application.JobPostingId, application.Version, 1),
            (result.Package!.ApplicationId, result.Package.JobPostingId, result.Package.ApplicationVersion, result.Package.PackageRevision));
        Assert.DoesNotContain(typeof(FinalizedApplicationPackageHandoff).GetProperties(), property =>
            property.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_package_is_not_ready_without_inventing_snapshot_blocker()
    {
        var application = FinalizedApplication();
        application.PackageStatus = "DRAFT";
        application.PackageRevision = 0;
        application.PackageJobPostingVersion = null;
        application.PackageManifestHash = null;
        application.PackageFinalizedAt = null;
        application.PackageFinalizedByAdminUserId = null;

        var result = new ApplicationSubmissionReadinessEvaluator().Evaluate(application, null);

        Assert.Equal(["PACKAGE_NOT_FINALIZED"], result.Blockers.Select(blocker => blocker.Code));
        Assert.Null(result.Package);
    }

    [Fact]
    public void Missing_or_invalid_snapshot_and_manifest_are_not_ready_in_deterministic_order()
    {
        var application = FinalizedApplication();
        application.PackageManifestHash = "bad";
        var missing = new ApplicationSubmissionReadinessEvaluator().Evaluate(application, null);
        var snapshot = Snapshot(application.Id);
        snapshot.ContentType = "image/png";
        var invalid = new ApplicationSubmissionReadinessEvaluator().Evaluate(application, snapshot);

        Assert.Equal(["PACKAGE_METADATA_INVALID", "PACKAGE_CV_MISSING"], missing.Blockers.Select(blocker => blocker.Code));
        Assert.Equal(["PACKAGE_METADATA_INVALID", "PACKAGE_CV_INVALID"], invalid.Blockers.Select(blocker => blocker.Code));
    }

    [Fact]
    public void Non_draft_application_is_not_invited_to_submit_again()
    {
        var application = FinalizedApplication();
        application.Status = "APPLIED";

        var result = new ApplicationSubmissionReadinessEvaluator().Evaluate(application, Snapshot(application.Id));

        Assert.Equal(["APPLICATION_NOT_DRAFT"], result.Blockers.Select(blocker => blocker.Code));
        Assert.False(result.IsReady);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task Query_reads_persisted_state_without_storage_ai_or_mutation()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var application = FinalizedApplication();
        db.Add(application);
        db.Add(Snapshot(application.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new GetApplicationSubmissionReadinessQueryHandler(db, new());
        var result = await handler.HandleAsync(new(application.Id));

        Assert.True(result.IsReady);
        Assert.False(db.ChangeTracker.HasChanges());
        var dependencies = typeof(GetApplicationSubmissionReadinessQueryHandler).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType.Namespace?.Contains(".AI", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Query_uses_existing_not_found_semantics()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetApplicationSubmissionReadinessQueryHandler(db, new()).HandleAsync(new(Guid.NewGuid())));
        Assert.Equal("JOB_APPLICATION_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task Query_ignores_removed_historical_snapshot_and_uses_the_active_snapshot()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var application = FinalizedApplication();
        var removed = Snapshot(application.Id);
        removed.RemovedAt = Now;
        var active = Snapshot(application.Id);
        db.AddRange(application, removed, active);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetApplicationSubmissionReadinessQueryHandler(db, new())
            .HandleAsync(new(application.Id));

        Assert.True(result.IsReady);
        Assert.Equal(active.Id, result.Package!.CvDocumentId);
    }

    private static JobApplication FinalizedApplication() => new()
    {
        Id = Guid.NewGuid(), JobPostingId = Guid.NewGuid(), Status = "DRAFT",
        PackageStatus = "FINALIZED", PackageRevision = 1, PackageJobPostingVersion = 4,
        PackageManifestHash = new string('b', 64), PackageFinalizedAt = Now,
        PackageFinalizedByAdminUserId = Guid.NewGuid(), Version = 7, CreatedAt = Now, UpdatedAt = Now,
    };

    private static JobApplicationDocument Snapshot(Guid applicationId) => new()
    {
        Id = Guid.NewGuid(), JobApplicationId = applicationId, DocumentType = "CV",
        VersionLabel = "Package revision 1", FileName = "cv.pdf", StorageKey = "applications/private/cv.pdf",
        ContentType = "application/pdf", FileSizeBytes = 1024, ContentHash = new string('a', 64),
        PackageRevision = 1, SourceCanonicalCvVersion = 3, Metadata = JsonDocument.Parse("{}"), CreatedAt = Now,
    };
}
