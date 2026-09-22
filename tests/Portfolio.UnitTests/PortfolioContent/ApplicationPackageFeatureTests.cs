using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ApplicationPackageFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Finalization_creates_one_immutable_snapshot_manifest_and_event()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakeStorage();
        var state = await SeedAsync(db, storage);

        var result = await Handler(db, storage).HandleAsync(Command(state));

        Assert.Equal("FINALIZED", result.Status);
        Assert.Equal(1, result.Revision);
        Assert.Equal(state.Job.Version, result.JobPostingVersion);
        Assert.Equal(64, result.ManifestHash!.Length);
        Assert.Equal(state.Cv.Version, result.Cv!.SourceCanonicalCvVersion);
        var application = await db.JobApplications.AsNoTracking().SingleAsync();
        Assert.Equal("FINALIZED", application.PackageStatus);
        Assert.Equal(1, application.PackageRevision);
        Assert.Equal(2, application.Version);
        var document = await db.JobApplicationDocuments.AsNoTracking().SingleAsync();
        Assert.StartsWith($"applications/{application.Id:N}/{document.Id:N}/", document.StorageKey);
        Assert.Equal(state.Bytes, storage.Objects[document.StorageKey!]);
        Assert.Equal("PACKAGE_FINALIZED", (await db.JobApplicationEvents.AsNoTracking().SingleAsync()).EventType);
        Assert.Equal(AdminId, application.PackageFinalizedByAdminUserId);
        Assert.Equal(Now, application.PackageFinalizedAt);
    }

    [Theory]
    [InlineData(2, 5, 3, "JOB_APPLICATION_VERSION_CONFLICT")]
    [InlineData(1, 6, 3, "JOB_POSTING_VERSION_CONFLICT")]
    [InlineData(1, 5, 4, "CANONICAL_CV_VERSION_CONFLICT")]
    public async Task Stale_versions_fail_before_snapshot_upload(int applicationVersion, int jobVersion, int cvVersion, string code)
    {
        await using var db = PublicPortfolioTests.CreateContext();var storage = new FakeStorage();var state = await SeedAsync(db, storage);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Handler(db, storage).HandleAsync(new(state.Application.Id, applicationVersion, jobVersion, cvVersion)));
        Assert.Equal(code, error.Code);Assert.Single(storage.Objects);Assert.Empty(await db.JobApplicationDocuments.ToListAsync());
    }

    [Theory]
    [InlineData("APPLIED", "APPROVED", false, "APPLICATION_NOT_DRAFT")]
    [InlineData("DRAFT", "SKIPPED", false, "JOB_NOT_APPROVED")]
    [InlineData("DRAFT", "APPROVED", true, "JOB_ARCHIVED")]
    public async Task Invalid_application_or_job_fails_closed(string applicationStatus, string selectionStatus, bool archived, string code)
    {
        await using var db = PublicPortfolioTests.CreateContext();var storage = new FakeStorage();var state = await SeedAsync(db, storage);state.Application.Status=applicationStatus;state.Job.SelectionStatus=selectionStatus;state.Job.ArchivedAt=archived?Now:null;await db.SaveChangesAsync();
        var error=await Assert.ThrowsAsync<ConflictException>(()=>Handler(db,storage).HandleAsync(Command(state)));Assert.Equal(code,error.Code);Assert.Single(storage.Objects);
    }

    [Fact]
    public async Task Already_finalized_and_duplicate_attempts_are_rejected_without_second_snapshot()
    {
        await using var db=PublicPortfolioTests.CreateContext();var storage=new FakeStorage();var state=await SeedAsync(db,storage);await Handler(db,storage).HandleAsync(Command(state));var count=storage.Objects.Count;
        var error=await Assert.ThrowsAsync<ConflictException>(()=>Handler(db,storage).HandleAsync(new(state.Application.Id,2,state.Job.Version,state.Cv.Version)));Assert.Equal("APPLICATION_PACKAGE_ALREADY_FINALIZED",error.Code);Assert.Equal(count,storage.Objects.Count);Assert.Single(await db.JobApplicationDocuments.ToListAsync());Assert.Single(await db.JobApplicationEvents.ToListAsync());
    }

    [Fact]
    public async Task Missing_object_size_hash_signature_and_oversized_stream_all_fail_without_mutation()
    {
        await AssertIntegrityFailure((state,storage)=>storage.Objects.Remove(state.Cv.StorageKey));
        await AssertIntegrityFailure((state,storage)=>storage.Objects[state.Cv.StorageKey]=Pdf("different-size"));
        await AssertIntegrityFailure((state,storage)=>storage.Objects[state.Cv.StorageKey]=state.Bytes.Select((value,index)=>(byte)(index==6?value+1:value)).ToArray());
        await AssertIntegrityFailure((state,storage)=>{var bytes=Encoding.UTF8.GetBytes("NOPE!same-size");state.Cv.FileSizeBytes=bytes.Length;state.Cv.ContentHash=Hash(bytes);storage.Objects[state.Cv.StorageKey]=bytes;});
        await AssertIntegrityFailure((state,storage)=>{var bytes=new byte[10*1024*1024+1];"%PDF-"u8.CopyTo(bytes);storage.Objects[state.Cv.StorageKey]=bytes;});
    }

    [Fact]
    public async Task Database_failure_compensates_snapshot_and_upload_failure_does_not_mutate_database()
    {
        await using(var db=PublicPortfolioTests.CreateContext()){var storage=new FakeStorage();var state=await SeedAsync(db,storage);db.FailSaveChanges=true;await Assert.ThrowsAsync<DbUpdateException>(()=>Handler(db,storage).HandleAsync(Command(state)));Assert.Single(storage.Objects);Assert.Single(storage.DeletedKeys);db.ChangeTracker.Clear();Assert.Equal("DRAFT",(await db.JobApplications.SingleAsync()).PackageStatus);Assert.Empty(await db.JobApplicationDocuments.ToListAsync());}
        await using(var db=PublicPortfolioTests.CreateContext()){var storage=new FakeStorage();var state=await SeedAsync(db,storage);storage.FailUpload=true;await Assert.ThrowsAsync<IOException>(()=>Handler(db,storage).HandleAsync(Command(state)));Assert.Single(storage.Objects);Assert.Equal("DRAFT",state.Application.PackageStatus);Assert.Empty(await db.JobApplicationDocuments.ToListAsync());}
    }

    [Fact]
    public void Manifest_is_canonical_deterministic_and_sensitive_to_immutable_input()
    {
        var application=Guid.NewGuid();var job=Guid.NewGuid();var document=Guid.NewGuid();var first=ApplicationPackageManifest.Serialize(application,job,5,1,document,"cv.pdf","application/pdf",42,new string('a',64));var second=ApplicationPackageManifest.Serialize(application,job,5,1,document,"cv.pdf","application/pdf",42,new string('a',64));var changed=ApplicationPackageManifest.Serialize(application,job,6,1,document,"cv.pdf","application/pdf",42,new string('a',64));
        Assert.Equal(first,second);Assert.Equal(ApplicationPackageManifest.Hash(first),ApplicationPackageManifest.Hash(second));Assert.NotEqual(ApplicationPackageManifest.Hash(first),ApplicationPackageManifest.Hash(changed));Assert.DoesNotContain("storage",first,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Package_read_and_content_contracts_do_not_expose_storage_key()
    {
        await using var db=PublicPortfolioTests.CreateContext();var storage=new FakeStorage();var state=await SeedAsync(db,storage);await Handler(db,storage).HandleAsync(Command(state));var package=await new GetApplicationPackageQueryHandler(db).HandleAsync(new(state.Application.Id));var content=await new GetApplicationPackageContentQueryHandler(db,storage,NullLogger<GetApplicationPackageContentQueryHandler>.Instance).HandleAsync(new(state.Application.Id));
        Assert.Equal(state.Bytes,content.Content);Assert.Equal("FINALIZED",package.Status);Assert.DoesNotContain(typeof(ApplicationPackageResult).GetProperties(),property=>property.Name=="StorageKey");Assert.DoesNotContain(typeof(FinalizedApplicationCvResult).GetProperties(),property=>property.Name=="StorageKey");
    }

    [Fact]
    public async Task Managed_snapshot_is_hidden_from_legacy_documents_and_cannot_be_removed()
    {
        await using var db=PublicPortfolioTests.CreateContext();var storage=new FakeStorage();var state=await SeedAsync(db,storage);await Handler(db,storage).HandleAsync(Command(state));var document=await db.JobApplicationDocuments.SingleAsync();var detail=await new GetJobApplicationQueryHandler(db).HandleAsync(new(state.Application.Id));Assert.Empty(detail.Documents);var error=await Assert.ThrowsAsync<ConflictException>(()=>new RemoveJobApplicationDocumentCommandHandler(db,new FixedTimeProvider(),new FakeCurrentUser(AdminId)).HandleAsync(new(state.Application.Id,document.Id)));Assert.Equal("APPLICATION_PACKAGE_DOCUMENT_IMMUTABLE",error.Code);Assert.Null(document.RemovedAt);
    }

    private static async Task AssertIntegrityFailure(Action<State,FakeStorage> arrange)
    {
        await using var db=PublicPortfolioTests.CreateContext();var storage=new FakeStorage();var state=await SeedAsync(db,storage);arrange(state,storage);await db.SaveChangesAsync();await Assert.ThrowsAsync<ServiceUnavailableException>(()=>Handler(db,storage).HandleAsync(Command(state)));Assert.Empty(await db.JobApplicationDocuments.ToListAsync());Assert.Equal("DRAFT",state.Application.PackageStatus);
    }

    private static FinalizeApplicationPackageCommandHandler Handler(ContentTestDbContext db,FakeStorage storage)=>new(db,storage,new FakeTransactionFactory(db),new FakeConflictDetector(),new ApplicationPackageReadinessEvaluator(),new FakeCurrentUser(AdminId),new FixedTimeProvider(),NullLogger<FinalizeApplicationPackageCommandHandler>.Instance);
    private static FinalizeApplicationPackageCommand Command(State state)=>new(state.Application.Id,state.Application.Version,state.Job.Version,state.Cv.Version);
    private static async Task<State> SeedAsync(ContentTestDbContext db,FakeStorage storage)
    {
        var bytes=Pdf("canonical");var job=new JobPosting{Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Hanoi",Description="Description",TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus="VERIFIED",SelectionStatus="APPROVED",Version=5,CreatedAt=Now,UpdatedAt=Now};var application=new JobApplication{Id=Guid.NewGuid(),JobPostingId=job.Id,Status="DRAFT",Version=1,CreatedAt=Now,UpdatedAt=Now};var cv=new CanonicalCv{Id=Guid.NewGuid(),SingletonKey=1,StorageKey="canonical-cv/source.pdf",FileName="CV.pdf",ContentType="application/pdf",FileSizeBytes=bytes.Length,ContentHash=Hash(bytes),Version=3,CreatedAt=Now,UpdatedAt=Now};db.AddRange(job,application,cv);await db.SaveChangesAsync();storage.Objects[cv.StorageKey]=bytes;return new(application,job,cv,bytes);
    }
    private static byte[] Pdf(string value)=>Encoding.UTF8.GetBytes("%PDF-1.7\n"+value);
    private static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private sealed record State(JobApplication Application,JobPosting Job,CanonicalCv Cv,byte[] Bytes);
    private sealed class FixedTimeProvider:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class FakeConflictDetector:IApplicationPackageConflictDetector{public bool IsManagedSnapshotConflict(DbUpdateException exception)=>false;}
    private sealed class FakeTransactionFactory(ContentTestDbContext db):IApplicationPackageFinalizationTransactionFactory
    {
        public async Task<IApplicationPackageFinalizationTransaction> BeginAsync(Guid applicationId,CancellationToken cancellationToken=default)=>new Transaction(await db.JobApplications.SingleOrDefaultAsync(x=>x.Id==applicationId,cancellationToken),await db.JobPostings.SingleOrDefaultAsync(x=>x.JobApplications.Any(a=>a.Id==applicationId),cancellationToken),await db.CanonicalCvs.SingleOrDefaultAsync(cancellationToken));
        private sealed class Transaction(JobApplication? application,JobPosting? job,CanonicalCv? cv):IApplicationPackageFinalizationTransaction{public JobApplication? Application{get;}=application;public JobPosting? JobPosting{get;}=job;public CanonicalCv? CanonicalCv{get;}=cv;public Task CommitAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    }
    private sealed class FakeStorage:IPrivateFileStorage
    {
        public Dictionary<string,byte[]> Objects{get;}=[];public List<string> DeletedKeys{get;}=[];public bool FailUpload{get;set;}
        public async Task UploadAsync(string key,Stream content,string contentType,CancellationToken cancellationToken=default){if(FailUpload)throw new IOException("upload failed");using var target=new MemoryStream();await content.CopyToAsync(target,cancellationToken);Objects[key]=target.ToArray();}
        public Task<Stream> OpenReadAsync(string key,long maximumBytes,CancellationToken cancellationToken=default)=>Task.FromResult<Stream>(new MemoryStream(Objects[key],writable:false));
        public Task DeleteAsync(string key,CancellationToken cancellationToken=default){DeletedKeys.Add(key);Objects.Remove(key);return Task.CompletedTask;}
    }
}
