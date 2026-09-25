using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Integrations.Gmail;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class FacebookEmailApplicationFeatureTests
{
    [Theory]
    [InlineData(74,100)]
    [InlineData(90,69)]
    public async Task Score_or_coverage_below_configured_gate_stops_before_sender_and_application(int score,int coverage)
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,"owner@example.com");var dispatcher=new FakeDispatcher(db,Fit(job.Id,score,coverage));var sender=new FakeSender();var notifications=new FakeNotifications();
        var result=await Handler(db,dispatcher,sender,notifications).HandleAsync(new(job.Id,Guid.NewGuid()));
        Assert.Equal(("BLOCKED","FIT_GATE_BLOCKED"),(result.Status,result.BlockerCode));Assert.Equal((0,0),(sender.ValidationCalls,sender.SendCalls));Assert.Empty(await db.JobApplications.ToListAsync());Assert.Equal("FIT_GATE_BLOCKED",notifications.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-email")]
    public async Task Missing_or_invalid_email_stops_without_sender_or_application(string? email)
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,email);var dispatcher=new FakeDispatcher(db,Fit(job.Id,90,90));var sender=new FakeSender();var notifications=new FakeNotifications();
        var result=await Handler(db,dispatcher,sender,notifications).HandleAsync(new(job.Id,Guid.NewGuid()));
        Assert.Equal("APPLICATION_EMAIL_MISSING",result.BlockerCode);Assert.Equal((0,0),(sender.ValidationCalls,sender.SendCalls));Assert.Empty(await db.JobApplications.ToListAsync());Assert.Equal("APPLICATION_EMAIL_MISSING",notifications.Code);
    }

    [Fact]
    public async Task Unsupported_source_is_rejected_before_fit_sender_or_network()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,"owner@example.com",source:RawJobPostingSources.CompanySite);var dispatcher=new FakeDispatcher(db,Fit(job.Id,90,90));var sender=new FakeSender();
        var error=await Assert.ThrowsAsync<ConflictException>(()=>Handler(db,dispatcher,sender,new FakeNotifications()).HandleAsync(new(job.Id,Guid.NewGuid())));
        Assert.Equal("EMAIL_APPLICATION_SOURCE_NOT_SUPPORTED",error.Code);Assert.Equal(0,dispatcher.Calls);Assert.Equal((0,0),(sender.ValidationCalls,sender.SendCalls));Assert.Empty(await db.JobApplications.ToListAsync());
    }

    [Fact]
    public async Task Test_mode_rejection_occurs_before_any_workflow_mutation()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,"recruiter@example.com");var dispatcher=new FakeDispatcher(db,Fit(job.Id,90,90));var sender=new FakeSender{Error=new ConflictException("EMAIL_RECIPIENT_NOT_ALLOWED","blocked")};
        var error=await Assert.ThrowsAsync<ConflictException>(()=>Handler(db,dispatcher,sender,new FakeNotifications()).HandleAsync(new(job.Id,Guid.NewGuid())));
        Assert.Equal("EMAIL_RECIPIENT_NOT_ALLOWED",error.Code);Assert.Empty(await db.JobApplications.ToListAsync());Assert.Equal(0,dispatcher.MutationCalls);
    }

    [Theory]
    [InlineData(RawJobPostingSources.Facebook)]
    [InlineData(RawJobPostingSources.Manual)]
    public async Task Valid_supported_source_flow_uses_one_idempotent_email_attempt_and_notification_failure_does_not_undo_applied(string source)
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,"owner@example.com",JobPostingSelectionStatuses.Approved,source);
        var app=new JobApplication{Id=Guid.NewGuid(),JobPostingId=job.Id,Status="DRAFT",Channel="EMAIL",ApplicationEmail="owner@example.com",PackageStatus="FINALIZED",PackageRevision=1,PackageManifestHash=new string('a',64),PackageJobPostingVersion=job.Version,PackageFinalizedAt=DateTimeOffset.UtcNow,PackageFinalizedByAdminUserId=Guid.NewGuid(),Version=2};db.JobApplications.Add(app);await db.SaveChangesAsync();
        var dispatcher=new FakeDispatcher(db,Fit(job.Id,90,90));var sender=new FakeSender();var notifications=new FakeNotifications{Throw=true};var requestId=Guid.NewGuid();
        var result=await Handler(db,dispatcher,sender,notifications).HandleAsync(new(job.Id,requestId));
        Assert.Equal("SUCCEEDED",result.Status);Assert.Equal(1,dispatcher.ExecuteCalls);Assert.Equal("APPLIED",(await db.JobApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Existing_terminal_idempotency_key_returns_without_fit_or_sender_or_retry()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=await SeedJobAsync(db,"owner@example.com",JobPostingSelectionStatuses.Approved);var requestId=Guid.NewGuid();
        var app=new JobApplication{Id=Guid.NewGuid(),JobPostingId=job.Id,Status="APPLIED",ApplicationEmail="owner@example.com",PackageStatus="FINALIZED",PackageRevision=1,PackageManifestHash=new string('a',64),Version=3};
        var attempt=new SubmissionAttempt{Id=Guid.NewGuid(),JobApplicationId=app.Id,Provider="EMAIL",Status="SUCCEEDED",IdempotencyKey=SubmissionAttemptIdempotency.Create(app.Id,1,app.PackageManifestHash,"EMAIL",requestId),PackageRevision=1,PackageManifestHash=app.PackageManifestHash,ApplicationVersionAtCreation=2,CreatedByAdminUserId=Guid.NewGuid(),Version=4};db.JobApplications.Add(app);db.SubmissionAttempts.Add(attempt);await db.SaveChangesAsync();
        var dispatcher=new FakeDispatcher(db,Fit(job.Id,90,90));var sender=new FakeSender();var result=await Handler(db,dispatcher,sender,new FakeNotifications()).HandleAsync(new(job.Id,requestId));
        Assert.Equal("SUCCEEDED",result.Status);Assert.Equal(0,dispatcher.Calls);Assert.Equal(0,sender.ValidationCalls);
    }

    [Fact]
    public async Task Dedicated_orchestration_uses_real_attempt_execution_and_applies_only_after_email_success()
    {
        await using var db=PublicPortfolioTests.CreateContext();var now=new DateTimeOffset(2026,9,24,5,0,0,TimeSpan.Zero);var bytes="%PDF-immutable-package"u8.ToArray();var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var job=await SeedJobAsync(db,"owner@example.com",JobPostingSelectionStatuses.Approved);job.VerificationStatus=JobPostingVerificationStatuses.Verified;job.Version=4;
        var app=new JobApplication{Id=Guid.NewGuid(),JobPostingId=job.Id,Status=JobApplicationStatuses.Draft,Channel=JobApplicationChannels.Email,ApplicationEmail="owner@example.com",PackageStatus=JobApplicationPackageStatuses.Finalized,PackageRevision=1,PackageManifestHash=new string('b',64),PackageJobPostingVersion=job.Version,PackageFinalizedAt=now,PackageFinalizedByAdminUserId=Guid.NewGuid(),Version=2,CreatedAt=now,UpdatedAt=now};
        var document=new JobApplicationDocument{Id=Guid.NewGuid(),JobApplicationId=app.Id,DocumentType="CV",VersionLabel="Package revision 1",FileName="CV Nguyễn Đình Phúc.pdf",StorageKey="private/package.pdf",ContentHash=hash,ContentType="application/pdf",FileSizeBytes=bytes.Length,PackageRevision=1,SourceCanonicalCvVersion=3,Metadata=JsonDocument.Parse("{}"),CreatedAt=now};
        db.AddRange(app,document,new Profile{Id=Guid.NewGuid(),SingletonKey=1,FullName="Nguyễn Đình Phúc"});await db.SaveChangesAsync();
        var user=new CurrentUser();var clock=new FixedTimeProvider(now);var createCore=new CreateSubmissionAttemptCommandHandler(db,new CreationFactory(db),new NeverConflict(),new(),user,clock);var approveCore=new ApproveSubmissionAttemptCommandHandler(db,user,clock);var transport=new RecordingSender();
        var gmailAdapter=new GmailSubmissionAdapter(db,new Storage(bytes),transport,new DeterministicApplicationEmailComposer(),Options.Create(EmailSettings()),NullLogger<GmailSubmissionAdapter>.Instance);
        var executeCore=new ExecuteSubmissionAttemptCommandHandler(db,new ExecutionFactory(db),[gmailAdapter],new(),user,clock);
        var dispatcher=new RealDispatcher(Fit(job.Id,90,90),new(createCore),new(approveCore),new(executeCore));var notifications=new FakeNotifications{Throw=true};

        var result=await new SubmitFacebookEmailApplicationCommandHandler(db,dispatcher,transport,notifications,Options.Create(new JobRecommendationOptions())).HandleAsync(new(job.Id,Guid.NewGuid()));

        Assert.Equal(SubmissionAttemptStatuses.Succeeded,result.Status);Assert.Equal(1,transport.SendCalls);Assert.Equal("owner@example.com",transport.Message!.Recipient);Assert.Equal(bytes,transport.Message.AttachmentContent);
        var saved=await db.JobApplications.SingleAsync();Assert.Equal(JobApplicationStatuses.Applied,saved.Status);Assert.NotNull(saved.AppliedAt);Assert.Equal("gmail-message-1",saved.ExternalApplicationId);
        var attempt=await db.SubmissionAttempts.Include(item=>item.Events).SingleAsync();Assert.Equal(["CREATED","APPROVED","SUBMITTING","SUCCEEDED"],attempt.Events.OrderBy(item=>item.OccurredAt).Select(item=>item.ToStatus));
        var statusEvent=Assert.Single(await db.JobApplicationEvents.Where(item=>item.EventType==JobApplicationEventTypes.StatusChanged).ToListAsync());Assert.Equal((JobApplicationStatuses.Draft,JobApplicationStatuses.Applied),(statusEvent.FromStatus,statusEvent.ToStatus));
    }

    private static SubmitFacebookEmailApplicationCommandHandler Handler(ContentTestDbContext db,FakeDispatcher dispatcher,FakeSender sender,FakeNotifications notifications)=>new(db,dispatcher,sender,notifications,Options.Create(new JobRecommendationOptions()));
    private static JobFitAnalysisResult Fit(Guid id,int score,int coverage)=>new(id,1,1,score,coverage,100,coverage,"PENDING",[],[],[],[],[]){Recommendation=score>=75&&coverage>=70?"RECOMMENDED":"NEEDS_REVIEW",Concerns=["Safe mismatch reason."]};
    private static async Task<JobPosting> SeedJobAsync(ContentTestDbContext db,string? email,string selection="PENDING_ANALYSIS",string source=RawJobPostingSources.Facebook)
    {var job=new JobPosting{Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Remote",Description="Role",TechnologyStack=JsonDocument.Parse("[]"),ApplicationEmail=email,SelectionStatus=selection,Version=1};db.JobPostings.Add(job);db.RawJobPostings.Add(new RawJobPosting{Id=Guid.NewGuid(),JobPostingId=job.Id,Source=source,RawContent="job",ContentHash=new string('a',64),IngestionStatus="NORMALIZED",Metadata=JsonDocument.Parse("{}")});await db.SaveChangesAsync();return job;}

    private sealed class FakeSender:IApplicationEmailSender{public int ValidationCalls;public int SendCalls;public Exception? Error;public void EnsureCanSend(string recipient){ValidationCalls++;if(Error is not null)throw Error;}public Task<SubmissionResult> SendAsync(ApplicationEmailMessage message,CancellationToken ct=default){SendCalls++;throw new NotSupportedException();}}
    private sealed class FakeNotifications:IAdminJobNotificationSender{public string? Code;public bool Throw;public Task SendAsync(string code,string message,Guid id,CancellationToken ct=default){Code=code;if(Throw)throw new InvalidOperationException();return Task.CompletedTask;}}
    private sealed class FakeDispatcher(ContentTestDbContext db,JobFitAnalysisResult fit):IRequestDispatcher
    {
        public int Calls;public int MutationCalls;public int ExecuteCalls;
        public async Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request,CancellationToken ct=default)
        {
            Calls++;object result=request switch
            {
                GetJobFitAnalysisQuery=>fit,
                CreateAuthorizedEmailSubmissionAttemptCommand command=>Create(command.Command),
                ApproveAuthorizedEmailSubmissionAttemptCommand command=>Attempt(command.AttemptId,"APPROVED",2),
                ExecuteAuthorizedEmailSubmissionAttemptCommand command=>await Execute(command.AttemptId),
                _=>throw new NotSupportedException(request.GetType().Name)
            };return (TResponse)result;
        }
        private SubmissionAttemptResult Create(CreateSubmissionAttemptCommand command){MutationCalls++;return Attempt(Guid.NewGuid(),"CREATED",1);}
        private async Task<SubmissionAttemptResult> Execute(Guid attemptId){MutationCalls++;ExecuteCalls++;var app=await db.JobApplications.SingleAsync();app.Status="APPLIED";app.AppliedAt=DateTimeOffset.UtcNow;app.Version++;await db.SaveChangesAsync();return Attempt(attemptId,"SUCCEEDED",4);}
        private static SubmissionAttemptResult Attempt(Guid id,string status,int version)=>new(id,Guid.NewGuid(),"EMAIL",status,1,new string('a',64),2,DateTimeOffset.UtcNow,Guid.NewGuid(),null,status=="SUCCEEDED"?DateTimeOffset.UtcNow:null,status=="SUCCEEDED"?"gmail-id":null,null,null,version,[]);
    }

    private sealed class RealDispatcher(JobFitAnalysisResult fit,CreateAuthorizedEmailSubmissionAttemptCommandHandler create,ApproveAuthorizedEmailSubmissionAttemptCommandHandler approve,ExecuteAuthorizedEmailSubmissionAttemptCommandHandler execute):IRequestDispatcher
    {public async Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request,CancellationToken ct=default){object result=request switch{GetJobFitAnalysisQuery=>fit,CreateAuthorizedEmailSubmissionAttemptCommand value=>await create.HandleAsync(value,ct),ApproveAuthorizedEmailSubmissionAttemptCommand value=>await approve.HandleAsync(value,ct),ExecuteAuthorizedEmailSubmissionAttemptCommand value=>await execute.HandleAsync(value,ct),_=>throw new NotSupportedException(request.GetType().Name)};return(TResponse)result;}}
    private sealed class CurrentUser:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");public string? Email=>"admin@example.com";}
    private sealed class FixedTimeProvider(DateTimeOffset now):TimeProvider{private long ticks;public override DateTimeOffset GetUtcNow()=>now.AddTicks(Interlocked.Add(ref ticks,10));}
    private sealed class NeverConflict:ISubmissionAttemptConflictDetector{public bool IsDuplicateIdempotencyKey(DbUpdateException exception)=>false;public bool IsNonRetryableAttemptConflict(DbUpdateException exception)=>false;}
    private sealed class CreationFactory(ContentTestDbContext db):ISubmissionAttemptCreationTransactionFactory
    {public async Task<ISubmissionAttemptCreationTransaction> BeginAsync(Guid id,CancellationToken ct=default)=>new Creation(await db.JobApplications.SingleOrDefaultAsync(x=>x.Id==id,ct),await db.JobApplicationDocuments.SingleOrDefaultAsync(x=>x.JobApplicationId==id&&x.PackageRevision==1&&x.RemovedAt==null,ct));private sealed class Creation(JobApplication? app,JobApplicationDocument? cv):ISubmissionAttemptCreationTransaction{public JobApplication? Application=>app;public JobApplicationDocument? ManagedCv=>cv;public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
    private sealed class ExecutionFactory(ContentTestDbContext db):ISubmissionAttemptExecutionTransactionFactory
    {public async Task<ISubmissionAttemptExecutionTransaction> BeginAsync(Guid id,CancellationToken ct=default){var attempt=await db.SubmissionAttempts.SingleOrDefaultAsync(x=>x.Id==id,ct);var app=attempt is null?null:await db.JobApplications.SingleAsync(x=>x.Id==attempt.JobApplicationId,ct);var cv=app is null?null:await db.JobApplicationDocuments.SingleOrDefaultAsync(x=>x.JobApplicationId==app.Id&&x.PackageRevision==1&&x.RemovedAt==null,ct);return new Execution(attempt,app,cv);}private sealed class Execution(SubmissionAttempt? attempt,JobApplication? app,JobApplicationDocument? cv):ISubmissionAttemptExecutionTransaction{public SubmissionAttempt? Attempt=>attempt;public JobApplication? Application=>app;public JobApplicationDocument? ManagedCv=>cv;public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
    private sealed class Storage(byte[] bytes):IPrivateFileStorage{public Task UploadAsync(string key,Stream content,string type,CancellationToken ct=default)=>throw new NotSupportedException();public Task<Stream> OpenReadAsync(string key,long maximum,CancellationToken ct=default)=>Task.FromResult<Stream>(new MemoryStream(bytes,false));public Task DeleteAsync(string key,CancellationToken ct=default)=>throw new NotSupportedException();}
    private sealed class RecordingSender:IApplicationEmailSender{public int ValidationCalls;public int SendCalls;public ApplicationEmailMessage? Message;public void EnsureCanSend(string recipient)=>ValidationCalls++;public Task<SubmissionResult> SendAsync(ApplicationEmailMessage message,CancellationToken ct=default){SendCalls++;Message=message;return Task.FromResult(new SubmissionResult(SubmissionOutcomes.Success,"gmail-message-1",null,null));}}
    private static EmailSubmissionOptions EmailSettings()=>new(){Enabled=true,TestMode=true,AllowedRecipients=["owner@example.com"],SenderEmail="sender@example.com",GoogleClientId="client",GoogleClientSecret="secret",GoogleRefreshToken="refresh"};
}
