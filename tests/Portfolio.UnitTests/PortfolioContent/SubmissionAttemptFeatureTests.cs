using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class SubmissionAttemptFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Valid_creation_is_idempotent_auditable_and_does_not_mutate_application_or_package()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var handler = Handler(db, state);
        var command = Command(state);

        var first = await handler.HandleAsync(command);
        var second = await handler.HandleAsync(command);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(("MANUAL", "CREATED", 1, state.Application.Version),
            (first.Provider, first.Status, first.PackageRevision, first.ApplicationVersionAtCreation));
        Assert.Single(await db.SubmissionAttempts.ToListAsync());
        var history = Assert.Single(first.Events);
        Assert.Equal((null, "CREATED", AdminId), (history.FromStatus, history.ToStatus, history.ActorAdminUserId));
        Assert.Equal("DRAFT", state.Application.Status);
        Assert.Null(state.Application.AppliedAt);
        Assert.Equal(1, state.Application.Version);
        Assert.Equal("FINALIZED", state.Application.PackageStatus);
        Assert.Equal(state.ManifestHash, state.Application.PackageManifestHash);
        Assert.Null(state.Snapshot.RemovedAt);
        Assert.Empty(await db.JobApplicationEvents.Where(item => item.EventType == "STATUS_CHANGED").ToListAsync());
        Assert.DoesNotContain(typeof(SubmissionAttemptResult).GetProperties(), property =>
            property.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public async Task Different_request_id_cannot_create_a_second_non_retryable_attempt_for_the_same_context()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var handler = Handler(db, state);
        await handler.HandleAsync(Command(state));

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
            Command(state) with { ClientRequestId = Guid.NewGuid() }));

        Assert.Equal("SUBMISSION_ATTEMPT_ACTIVE_EXISTS", error.Code);
        Assert.Single(await db.SubmissionAttempts.ToListAsync());
        Assert.Single(await db.SubmissionAttemptEvents.ToListAsync());
    }

    [Fact]
    public async Task Failed_attempt_allows_an_explicit_retry_but_unknown_and_succeeded_do_not()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var handler = Handler(db, state);
        var failed = await handler.HandleAsync(Command(state));
        var entity = await db.SubmissionAttempts.SingleAsync(item => item.Id == failed.Id);
        entity.Status = SubmissionAttemptStatuses.Failed;entity.StartedAt=Now;entity.CompletedAt=Now;entity.FailureCode="REJECTED";
        await db.SaveChangesAsync();
        var retry = await handler.HandleAsync(Command(state) with { ClientRequestId = Guid.NewGuid() });
        Assert.NotEqual(failed.Id, retry.Id);

        entity = await db.SubmissionAttempts.SingleAsync(item => item.Id == retry.Id);
        entity.Status = SubmissionAttemptStatuses.Unknown;entity.StartedAt=Now;entity.CompletedAt=Now;
        await db.SaveChangesAsync();
        var unknown = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(state) with { ClientRequestId = Guid.NewGuid() }));
        Assert.Equal("SUBMISSION_ATTEMPT_ACTIVE_EXISTS", unknown.Code);

        entity.Status = SubmissionAttemptStatuses.Succeeded;entity.FailureCode=null;
        await db.SaveChangesAsync();
        var succeeded = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(state) with { ClientRequestId = Guid.NewGuid() }));
        Assert.Equal("SUBMISSION_ATTEMPT_ACTIVE_EXISTS", succeeded.Code);
    }

    [Fact]
    public async Task Missing_application_uses_existing_not_found_semantics()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var handler = new CreateSubmissionAttemptCommandHandler(db,
            new FakeTransactionFactory(null, null), new NeverConflict(), new(), new CurrentUser(), new FixedTimeProvider());
        var error = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
            new(Guid.NewGuid(), "MANUAL", Guid.NewGuid(), 1, 1, new string('a', 64))));
        Assert.Equal("JOB_APPLICATION_NOT_FOUND", error.Code);
    }

    [Theory]
    [InlineData("application", "APPLICATION_NOT_DRAFT")]
    [InlineData("package", "PACKAGE_NOT_FINALIZED")]
    [InlineData("snapshot", "PACKAGE_CV_MISSING")]
    public async Task Authoritative_readiness_failures_reject_without_attempt(string invalid, string expectedCode)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        if (invalid == "application") state.Application.Status = "APPLIED";
        if (invalid == "package") { state.Application.PackageStatus = "DRAFT";state.Application.PackageRevision=0;state.Application.PackageJobPostingVersion=null;state.Application.PackageManifestHash=null;state.Application.PackageFinalizedAt=null;state.Application.PackageFinalizedByAdminUserId=null; }
        var snapshot = invalid == "snapshot" ? null : state.Snapshot;
        var handler = Handler(db, state with { Snapshot = snapshot! });

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(state)));

        Assert.Equal(expectedCode, error.Code);
        Assert.Empty(await db.SubmissionAttempts.ToListAsync());
    }

    [Fact]
    public async Task Invalid_snapshot_stale_version_revision_and_manifest_are_rejected()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        state.Snapshot.ContentType = "image/png";
        var invalid = await Assert.ThrowsAsync<ConflictException>(() => Handler(db, state).HandleAsync(Command(state)));
        Assert.Equal("PACKAGE_CV_INVALID", invalid.Code);
        state.Snapshot.ContentType = "application/pdf";
        var stale = await Assert.ThrowsAsync<ConflictException>(() => Handler(db, state).HandleAsync(Command(state) with { ExpectedApplicationVersion = 2 }));
        Assert.Equal("JOB_APPLICATION_VERSION_CONFLICT", stale.Code);
        var revision = await Assert.ThrowsAsync<ConflictException>(() => Handler(db, state).HandleAsync(Command(state) with { ExpectedPackageRevision = 2 }));
        Assert.Equal("SUBMISSION_PACKAGE_REVISION_CONFLICT", revision.Code);
        var manifest = await Assert.ThrowsAsync<ConflictException>(() => Handler(db, state).HandleAsync(Command(state) with { ExpectedManifestHash = new string('c', 64) }));
        Assert.Equal("SUBMISSION_PACKAGE_MANIFEST_CONFLICT", manifest.Code);
    }

    [Fact]
    public async Task Unsupported_provider_and_invalid_request_are_rejected()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var error = await Assert.ThrowsAsync<ValidationException>(() => Handler(db, state).HandleAsync(Command(state) with { Provider = "LINKEDIN" }));
        Assert.Contains("provider", error.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(await new CreateSubmissionAttemptCommandValidator().ValidateAsync(
            new(Guid.Empty, "", Guid.Empty, 0, 2, "bad")));
    }

    [Fact]
    public async Task Generic_creation_rejects_email_before_starting_a_submission_transaction()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var transaction = new CountingTransactionFactory(state.Application, state.Snapshot);
        var handler = new CreateSubmissionAttemptCommandHandler(db, transaction, new NeverConflict(), new(),
            new CurrentUser(), new FixedTimeProvider());

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
            Command(state) with { Provider = SubmissionProviders.Email }));

        Assert.Equal("EMAIL_SUBMISSION_REQUIRES_ORCHESTRATION", error.Code);
        Assert.Equal(0, transaction.Calls);
        Assert.Empty(await db.SubmissionAttempts.ToListAsync());
    }

    [Fact]
    public void State_machine_is_explicit_and_terminal_or_ambiguous_outcomes_never_auto_retry()
    {
        Assert.True(SubmissionAttemptStateMachine.CanTransition("CREATED", "APPROVED"));
        Assert.True(SubmissionAttemptStateMachine.CanTransition("APPROVED", "SUBMITTING"));
        Assert.True(SubmissionAttemptStateMachine.CanTransition("SUBMITTING", "SUCCEEDED"));
        Assert.True(SubmissionAttemptStateMachine.CanTransition("SUBMITTING", "FAILED"));
        Assert.True(SubmissionAttemptStateMachine.CanTransition("SUBMITTING", "UNKNOWN"));
        Assert.False(SubmissionAttemptStateMachine.CanTransition("FAILED", "SUBMITTING"));
        Assert.False(SubmissionAttemptStateMachine.CanTransition("UNKNOWN", "SUBMITTING"));
        Assert.False(SubmissionAttemptStateMachine.CanTransition("SUCCEEDED", "SUBMITTING"));
        Assert.False(SubmissionAttemptStateMachine.CanTransition("CREATED", "SUCCEEDED"));
    }

    [Fact]
    public void Adapter_request_and_result_contracts_are_provider_neutral_safe_and_distinguish_unknown()
    {
        Assert.DoesNotContain(typeof(SubmissionRequest).GetProperties(), property =>
            property.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType == typeof(byte[]));
        var failure = new SubmissionResult(SubmissionOutcomes.Failure, null, "REJECTED", "Safe diagnostic");
        var unknown = new SubmissionResult(SubmissionOutcomes.Unknown, null, "TIMEOUT", "Outcome could not be determined");
        Assert.NotEqual(failure.Outcome, unknown.Outcome);
        Assert.Contains(nameof(ISubmissionAdapter.SubmitAsync), typeof(ISubmissionAdapter).GetMethods().Select(method => method.Name));
    }

    [Fact]
    public void Ef_model_enforces_idempotency_relationships_and_attempt_concurrency()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=test;Password=test", options => options.UseVector()).Options);
        var attempt = db.Model.FindEntityType(typeof(SubmissionAttempt))!;
        Assert.True(attempt.FindProperty(nameof(SubmissionAttempt.Version))!.IsConcurrencyToken);
        Assert.Contains(attempt.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(SubmissionAttempt.IdempotencyKey)]));
        Assert.Contains(attempt.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(SubmissionAttempt.JobApplicationId), nameof(SubmissionAttempt.PackageRevision), nameof(SubmissionAttempt.Provider)]) &&
            index.GetFilter() == "status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'UNKNOWN')");
        Assert.Contains(attempt.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(JobApplication));
        var history = db.Model.FindEntityType(typeof(SubmissionAttemptEvent))!;
        Assert.Contains(history.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(SubmissionAttempt));
    }

    [Fact]
    public async Task Read_queries_are_deterministic_and_safe()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var created = await Handler(db, state).HandleAsync(Command(state));
        var list = await new GetSubmissionAttemptsQueryHandler(db).HandleAsync(new(state.Application.Id));
        var detail = await new GetSubmissionAttemptQueryHandler(db).HandleAsync(new(created.Id));
        Assert.Equal(created.Id, Assert.Single(list).Id);
        Assert.Equal((created.Id, created.Provider, created.Status), (detail.Id, detail.Provider, detail.Status));
        Assert.Equal(created.Events, detail.Events);
    }

    private static CreateSubmissionAttemptCommandHandler Handler(ContentTestDbContext db, State state) => new(
        db, new FakeTransactionFactory(state.Application, state.Snapshot), new NeverConflict(), new(),
        new CurrentUser(), new FixedTimeProvider());
    private static CreateSubmissionAttemptCommand Command(State state) => new(
        state.Application.Id, "MANUAL", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        state.Application.Version, 1, state.ManifestHash);

    private static async Task<State> SeedAsync(ContentTestDbContext db)
    {
        var manifest = new string('b', 64);
        var job = new JobPosting { Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Hanoi",Description="Description",TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus="VERIFIED",SelectionStatus="APPROVED",Version=4,CreatedAt=Now,UpdatedAt=Now };
        var application = new JobApplication { Id=Guid.NewGuid(),JobPostingId=job.Id,Status="DRAFT",PackageStatus="FINALIZED",PackageRevision=1,PackageJobPostingVersion=4,PackageManifestHash=manifest,PackageFinalizedAt=Now,PackageFinalizedByAdminUserId=AdminId,Version=1,CreatedAt=Now,UpdatedAt=Now };
        var snapshot = new JobApplicationDocument { Id=Guid.NewGuid(),JobApplicationId=application.Id,DocumentType="CV",VersionLabel="Package revision 1",FileName="cv.pdf",StorageKey="applications/private/cv.pdf",ContentHash=new string('a',64),ContentType="application/pdf",FileSizeBytes=1024,PackageRevision=1,SourceCanonicalCvVersion=3,Metadata=JsonDocument.Parse("{}"),CreatedAt=Now };
        db.AddRange(job, application, snapshot);await db.SaveChangesAsync();
        return new(application, snapshot, manifest);
    }

    private sealed record State(JobApplication Application, JobApplicationDocument Snapshot, string ManifestHash);
    private sealed class FakeTransactionFactory(JobApplication? application, JobApplicationDocument? snapshot) : ISubmissionAttemptCreationTransactionFactory
    {
        public Task<ISubmissionAttemptCreationTransaction> BeginAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult<ISubmissionAttemptCreationTransaction>(new Transaction(application, snapshot));
        private sealed class Transaction(JobApplication? application, JobApplicationDocument? snapshot) : ISubmissionAttemptCreationTransaction
        { public JobApplication? Application { get; }=application;public JobApplicationDocument? ManagedCv { get; }=snapshot;public Task CommitAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
    }
    private sealed class CountingTransactionFactory(JobApplication? application, JobApplicationDocument? snapshot) : ISubmissionAttemptCreationTransactionFactory
    {
        public int Calls { get; private set; }
        public Task<ISubmissionAttemptCreationTransaction> BeginAsync(Guid applicationId, CancellationToken cancellationToken = default)
        { Calls++;return new FakeTransactionFactory(application,snapshot).BeginAsync(applicationId,cancellationToken); }
    }
    private sealed class NeverConflict : ISubmissionAttemptConflictDetector { public bool IsDuplicateIdempotencyKey(DbUpdateException exception) => false;public bool IsNonRetryableAttemptConflict(DbUpdateException exception)=>false; }
    private sealed class CurrentUser : ICurrentUser { public bool IsAuthenticated=>true;public Guid? AdminUserId=>AdminId;public string? Email=>"admin@example.com"; }
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow()=>Now; }
}
