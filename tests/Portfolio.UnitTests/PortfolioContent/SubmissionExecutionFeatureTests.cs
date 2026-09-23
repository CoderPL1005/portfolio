using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class SubmissionExecutionFeatureTests
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approval_transitions_created_once_with_actor_and_does_not_mutate_application()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db, SubmissionAttemptStatuses.Created, 1, approvedEvent: false);
        var handler = new ApproveSubmissionAttemptCommandHandler(db, new CurrentUser(), new FixedTimeProvider());

        var result = await handler.HandleAsync(new(state.Attempt.Id, 1));

        Assert.Equal((SubmissionAttemptStatuses.Approved, 2), (result.Status, result.Version));
        Assert.Equal([SubmissionAttemptStatuses.Created, SubmissionAttemptStatuses.Approved],
            result.Events.Select(item => item.ToStatus));
        var approval = result.Events.Last();
        Assert.Equal((SubmissionAttemptStatuses.Created, AdminId), (approval.FromStatus, approval.ActorAdminUserId));
        Assert.Equal((JobApplicationStatuses.Draft, 1, null),
            (state.Application.Status, state.Application.Version, state.Application.AppliedAt));
        Assert.Empty(await db.JobApplicationEvents.ToListAsync());
    }

    [Fact]
    public async Task Approval_rejects_stale_or_non_created_attempt_without_an_event()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db, SubmissionAttemptStatuses.Created, 1, approvedEvent: false);
        var handler = new ApproveSubmissionAttemptCommandHandler(db, new CurrentUser(), new FixedTimeProvider());
        var stale = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new(state.Attempt.Id, 2)));
        Assert.Equal("SUBMISSION_ATTEMPT_VERSION_CONFLICT", stale.Code);
        state.Attempt.Status = SubmissionAttemptStatuses.Approved; state.Attempt.Version = 2; await db.SaveChangesAsync();
        var repeated = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new(state.Attempt.Id, 2)));
        Assert.Equal("SUBMISSION_ATTEMPT_STATE_CONFLICT", repeated.Code);
        Assert.Single(await db.SubmissionAttemptEvents.ToListAsync());
    }

    [Fact]
    public async Task Missing_adapter_including_manual_leaves_approved_attempt_unclaimed()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        state.Attempt.Provider = SubmissionProviders.Manual; await db.SaveChangesAsync();
        var factory = new TransactionFactory(state);
        var handler = Handler(db, factory, []);

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new(state.Attempt.Id, 2)));

        Assert.Equal("SUBMISSION_ADAPTER_NOT_AVAILABLE", error.Code);
        Assert.Equal((SubmissionAttemptStatuses.Approved, 2, null),
            (state.Attempt.Status, state.Attempt.Version, state.Attempt.StartedAt));
        Assert.Equal(2, await db.SubmissionAttemptEvents.CountAsync());
    }

    [Fact]
    public async Task Success_claims_outside_external_work_then_atomically_succeeds_and_applies_application()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        var factory = new TransactionFactory(state);
        var adapter = new Adapter(SubmissionProviders.Email, (request, _) =>
        {
            Assert.False(factory.TransactionActive);
            Assert.Equal((state.Snapshot.Id, state.ManifestHash), (request.CvDocumentId, request.PackageManifestHash));
            return Task.FromResult(new SubmissionResult(SubmissionOutcomes.Success, " provider-123 ", null, null));
        });

        var result = await Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2));

        Assert.Equal((SubmissionAttemptStatuses.Succeeded, 4, "provider-123"),
            (result.Status, result.Version, result.ProviderSubmissionId));
        Assert.Equal(["CREATED", "APPROVED", "SUBMITTING", "SUCCEEDED"], result.Events.Select(item => item.ToStatus));
        Assert.Equal(JobApplicationStatuses.Applied, state.Application.Status);
        Assert.Equal("provider-123", state.Application.ExternalApplicationId);
        Assert.Equal(2, state.Application.Version);
        var statusEvent = Assert.Single(await db.JobApplicationEvents.ToListAsync());
        Assert.Equal((JobApplicationEventTypes.StatusChanged, "DRAFT", "APPLIED"),
            (statusEvent.EventType, statusEvent.FromStatus, statusEvent.ToStatus));
        Assert.Equal(1, adapter.CallCount);
        Assert.Equal(2, factory.CommitCount);
    }

    [Fact]
    public async Task Success_without_provider_id_does_not_clear_existing_external_application_id()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db);
        state.Application.ExternalApplicationId = "existing-external-id";
        await db.SaveChangesAsync();
        var factory = new TransactionFactory(state);
        var adapter = new Adapter(SubmissionProviders.Email, (_, _) => Task.FromResult(
            new SubmissionResult(SubmissionOutcomes.Success, null, null, null)));

        var result = await Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2));

        Assert.Equal(SubmissionAttemptStatuses.Succeeded, result.Status);
        Assert.Null(result.ProviderSubmissionId);
        Assert.Equal("existing-external-id", state.Application.ExternalApplicationId);
        Assert.Equal(JobApplicationStatuses.Applied, state.Application.Status);
    }

    [Theory]
    [InlineData("FAILURE", "FAILED")]
    [InlineData("UNKNOWN", "UNKNOWN")]
    public async Task Non_success_outcomes_do_not_apply_application(string outcome, string expectedStatus)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); var factory = new TransactionFactory(state);
        var adapter = new Adapter(SubmissionProviders.Email, (_, _) => Task.FromResult(
            new SubmissionResult(outcome, null, "provider rejected!", " Safe diagnostic ")));

        var result = await Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("PROVIDERREJECTED", result.FailureCode);
        Assert.Equal("Safe diagnostic", result.FailureMessage);
        Assert.Equal((JobApplicationStatuses.Draft, 1, null),
            (state.Application.Status, state.Application.Version, state.Application.AppliedAt));
        Assert.Empty(await db.JobApplicationEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adapter_exception_or_cancellation_after_claim_is_conservatively_unknown(bool cancel)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); var factory = new TransactionFactory(state);
        var adapter = new Adapter(SubmissionProviders.Email, (_, _) => cancel
            ? Task.FromException<SubmissionResult>(new OperationCanceledException())
            : Task.FromException<SubmissionResult>(new InvalidOperationException("private provider response")));

        var result = await Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2));

        Assert.Equal(SubmissionAttemptStatuses.Unknown, result.Status);
        Assert.StartsWith("ADAPTER_EXECUTION_", result.FailureCode);
        Assert.DoesNotContain("private provider response", result.FailureMessage ?? string.Empty);
        Assert.Equal(JobApplicationStatuses.Draft, state.Application.Status);
    }

    [Fact]
    public async Task Stale_application_or_package_blocks_before_claim_and_adapter_invocation()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); state.Application.Version = 2; await db.SaveChangesAsync();
        var factory = new TransactionFactory(state); var adapter = Adapter.Success();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2)));

        Assert.Equal("SUBMISSION_APPLICATION_CHANGED", error.Code);
        Assert.Equal(0, adapter.CallCount);
        Assert.Equal(SubmissionAttemptStatuses.Approved, state.Attempt.Status);
    }

    [Fact]
    public async Task Changed_manifest_blocks_before_claim_and_adapter_invocation()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); state.Application.PackageManifestHash = new string('d',64); await db.SaveChangesAsync();
        var factory = new TransactionFactory(state); var adapter = Adapter.Success();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2)));

        Assert.Equal("SUBMISSION_PACKAGE_MANIFEST_CONFLICT", error.Code);
        Assert.Equal(0, adapter.CallCount);
        Assert.Equal(SubmissionAttemptStatuses.Approved, state.Attempt.Status);
    }

    [Theory]
    [InlineData("CREATED",1)]
    [InlineData("FAILED",4)]
    [InlineData("UNKNOWN",4)]
    [InlineData("SUCCEEDED",4)]
    public async Task Execute_rejects_every_non_approved_state_without_invoking_adapter(string status,int version)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db,status,version);var factory=new TransactionFactory(state);var adapter=Adapter.Success();
        var error=await Assert.ThrowsAsync<ConflictException>(()=>Handler(db,factory,[adapter]).HandleAsync(new(state.Attempt.Id,version)));
        Assert.Equal("SUBMISSION_ATTEMPT_STATE_CONFLICT",error.Code);Assert.Equal(0,adapter.CallCount);
    }

    [Fact]
    public async Task A_second_execute_cannot_invoke_adapter_again_and_terminal_attempt_cannot_retry()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); var factory = new TransactionFactory(state); var adapter = Adapter.Success();
        var handler = Handler(db, factory, [adapter]);
        var result = await handler.HandleAsync(new(state.Attempt.Id, 2));

        var terminal = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new(state.Attempt.Id, result.Version)));

        Assert.Equal("SUBMISSION_ATTEMPT_STATE_CONFLICT", terminal.Code);
        Assert.Equal(1, adapter.CallCount);
        Assert.Equal(4, await db.SubmissionAttemptEvents.CountAsync());
    }

    [Fact]
    public async Task Provider_success_with_changed_local_state_becomes_unknown_without_applying()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var state = await SeedAsync(db); var factory = new TransactionFactory(state);
        var adapter = new Adapter(SubmissionProviders.Email, (_, _) =>
        {
            state.Application.Version++;
            return Task.FromResult(new SubmissionResult(SubmissionOutcomes.Success, "external-1", null, null));
        });

        var result = await Handler(db, factory, [adapter]).HandleAsync(new(state.Attempt.Id, 2));

        Assert.Equal(SubmissionAttemptStatuses.Unknown, result.Status);
        Assert.Equal("LOCAL_STATE_CHANGED_AFTER_SUBMISSION", result.FailureCode);
        Assert.Equal(JobApplicationStatuses.Draft, state.Application.Status);
    }

    [Fact]
    public async Task Contracts_and_validation_are_safe()
    {
        Assert.DoesNotContain(typeof(SubmissionAttemptResult).GetProperties(), property =>
            property.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) || property.PropertyType == typeof(byte[]));
        Assert.NotEmpty(await new ApproveSubmissionAttemptCommandValidator().ValidateAsync(new(Guid.Empty, 0)));
        Assert.NotEmpty(await new ExecuteSubmissionAttemptCommandValidator().ValidateAsync(new(Guid.Empty, 0)));
    }

    private static ExecuteSubmissionAttemptCommandHandler Handler(
        ContentTestDbContext db, TransactionFactory factory, IEnumerable<ISubmissionAdapter> adapters) =>
        new(db, factory, adapters, new(), new CurrentUser(), new FixedTimeProvider());

    private static async Task<State> SeedAsync(
        ContentTestDbContext db,
        string status = SubmissionAttemptStatuses.Approved,
        int version = 2,
        bool approvedEvent = true)
    {
        var manifest = new string('b', 64);
        var job = new JobPosting { Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Hanoi",Description="Description",TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus="VERIFIED",SelectionStatus="APPROVED",Version=4,CreatedAt=Now,UpdatedAt=Now };
        var application = new JobApplication { Id=Guid.NewGuid(),JobPostingId=job.Id,Status="DRAFT",ApplicationEmail="jobs@example.com",ApplicationUrl="https://example.com/jobs/1",PackageStatus="FINALIZED",PackageRevision=1,PackageJobPostingVersion=4,PackageManifestHash=manifest,PackageFinalizedAt=Now,PackageFinalizedByAdminUserId=AdminId,Version=1,CreatedAt=Now,UpdatedAt=Now };
        var snapshot = new JobApplicationDocument { Id=Guid.NewGuid(),JobApplicationId=application.Id,DocumentType="CV",VersionLabel="Package revision 1",FileName="cv.pdf",StorageKey="applications/private/cv.pdf",ContentHash=new string('a',64),ContentType="application/pdf",FileSizeBytes=1024,PackageRevision=1,SourceCanonicalCvVersion=3,Metadata=JsonDocument.Parse("{}"),CreatedAt=Now };
        var attempt = new SubmissionAttempt { Id=Guid.NewGuid(),JobApplicationId=application.Id,Provider=SubmissionProviders.Email,Status=status,IdempotencyKey=new string('c',64),PackageRevision=1,PackageManifestHash=manifest,ApplicationVersionAtCreation=1,CreatedAt=Now,CreatedByAdminUserId=AdminId,Version=version };
        var created = new SubmissionAttemptEvent { Id=Guid.NewGuid(),SubmissionAttemptId=attempt.Id,ToStatus="CREATED",ActorAdminUserId=AdminId,OccurredAt=Now,CreatedAt=Now };
        db.AddRange(job,application,snapshot,attempt,created);
        if (approvedEvent) db.SubmissionAttemptEvents.Add(new(){Id=Guid.NewGuid(),SubmissionAttemptId=attempt.Id,FromStatus="CREATED",ToStatus="APPROVED",ActorAdminUserId=AdminId,OccurredAt=Now.AddTicks(10),CreatedAt=Now.AddTicks(10)});
        await db.SaveChangesAsync();
        return new(application,snapshot,attempt,manifest);
    }

    private sealed record State(JobApplication Application,JobApplicationDocument Snapshot,SubmissionAttempt Attempt,string ManifestHash);
    private sealed class CurrentUser : ICurrentUser { public bool IsAuthenticated=>true;public Guid? AdminUserId=>AdminId;public string? Email=>"admin@example.com"; }
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class TransactionFactory(State state) : ISubmissionAttemptExecutionTransactionFactory
    {
        public bool TransactionActive { get; private set; }
        public int CommitCount { get; private set; }
        public Task<ISubmissionAttemptExecutionTransaction> BeginAsync(Guid attemptId,CancellationToken cancellationToken=default)
        {
            TransactionActive=true;
            return Task.FromResult<ISubmissionAttemptExecutionTransaction>(new Transaction(this,attemptId==state.Attempt.Id?state:null));
        }
        private sealed class Transaction(TransactionFactory owner,State? state):ISubmissionAttemptExecutionTransaction
        {
            public SubmissionAttempt? Attempt=>state?.Attempt;public JobApplication? Application=>state?.Application;public JobApplicationDocument? ManagedCv=>state?.Snapshot;
            public Task CommitAsync(CancellationToken cancellationToken=default){owner.CommitCount++;return Task.CompletedTask;}
            public ValueTask DisposeAsync(){owner.TransactionActive=false;return ValueTask.CompletedTask;}
        }
    }
    private sealed class Adapter(string provider,Func<SubmissionRequest,CancellationToken,Task<SubmissionResult>> submit):ISubmissionAdapter
    {
        public string Provider=>provider;public SubmissionAdapterCapabilities Capabilities=>new(true,false,false,false,true);public int CallCount{get;private set;}
        public bool Supports(SubmissionRequest request)=>true;
        public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request,CancellationToken cancellationToken=default){CallCount++;return await submit(request,cancellationToken);}
        public static Adapter Success()=>new(SubmissionProviders.Email,(_,_)=>Task.FromResult(new SubmissionResult(SubmissionOutcomes.Success,"external-1",null,null)));
    }
}
