using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobHuntingFeatureTests
{
    private static readonly DateTimeOffset Now=new(2026,9,15,12,0,0,TimeSpan.Zero);
    private static readonly Guid AdminId=Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]public async Task Manual_create_persists_raw_and_canonical_with_server_owned_values()
    {
        await using var db=PublicPortfolioTests.CreateContext();var handler=new CreateJobPostingCommandHandler(db,new FixedTimeProvider(Now));var result=await handler.HandleAsync(Create("ext-1","https://EXAMPLE.com:443/jobs/1","  first\r\nline  "));
        var raw=await db.RawJobPostings.SingleAsync();var job=await db.JobPostings.SingleAsync();Assert.Equal(job.Id,raw.JobPostingId);Assert.Equal("NORMALIZED",raw.IngestionStatus);Assert.Null(raw.IngestionKey);Assert.Equal("PENDING",job.VerificationStatus);Assert.Equal("PENDING_ANALYSIS",job.SelectionStatus);Assert.Equal(1,job.Version);Assert.Equal(64,raw.ContentHash.Length);Assert.Equal(64,raw.SourceUrlHash!.Length);Assert.Equal(64,raw.CompanyTitleFingerprint!.Length);Assert.Equal("  first\r\nline  ",raw.RawContent);Assert.Equal(result.Id,job.Id);
        await Assert.ThrowsAsync<ConflictException>(()=>handler.HandleAsync(Create("ext-1","https://different.example/jobs/2","other")));
        var second=await handler.HandleAsync(Create("ext-2","https://example.com/jobs/2","  first\r\nline  "));Assert.NotEqual(result.Id,second.Id);Assert.Equal(2,await db.JobPostings.CountAsync());await Assert.ThrowsAsync<ConflictException>(()=>handler.HandleAsync(Create("ext-3","https://EXAMPLE.com:443/jobs/2","third")));
    }

    [Fact]public async Task Job_list_filters_searches_archived_and_orders_deterministically()
    {
        await using var db=PublicPortfolioTests.CreateContext();var first=Posting("Alpha","Developer",Now,Guid.Parse("00000000-0000-0000-0000-000000000001"));var second=Posting("Beta","Engineer",Now,Guid.Parse("00000000-0000-0000-0000-000000000002"));second.ArchivedAt=Now;db.JobPostings.AddRange(second,first);db.RawJobPostings.AddRange(Raw(first.Id,"MANUAL"),Raw(second.Id,"TOPCV"));db.JobApplications.Add(new JobApplication{Id=Guid.NewGuid(),JobPostingId=first.Id,Status="DRAFT",Version=1,CreatedAt=Now,UpdatedAt=Now});await db.SaveChangesAsync();var all=await new GetJobPostingsQueryHandler(db).HandleAsync(new(1,20,null,null,null,null,null));Assert.Equal(new[]{first.Id,second.Id},all.Items.Select(x=>x.Id));Assert.Equal(1,all.Items[0].ApplicationCount);var filtered=await new GetJobPostingsQueryHandler(db).HandleAsync(new(1,20,"beta","TOPCV",null,null,true));Assert.Equal(second.Id,filtered.Items.Single().Id);await Assert.ThrowsAsync<NotFoundException>(()=>new GetJobPostingQueryHandler(db).HandleAsync(new(Guid.NewGuid())));
    }

    [Fact]public async Task Job_updates_are_versioned_and_preserve_raw_identity_and_hashes()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Old","Role",Now);var raw=Raw(job.Id,"MANUAL");db.AddRange(job,raw);await db.SaveChangesAsync();var update=new UpdateJobPostingCommandHandler(db,new FixedTimeProvider(Now.AddHours(1)));var changed=await update.HandleAsync(Update(job.Id,1,"New"));Assert.Equal("New",changed.CompanyName);Assert.Equal(2,changed.Version);Assert.Equal("hash",raw.ContentHash);await Assert.ThrowsAsync<ConflictException>(()=>update.HandleAsync(Update(job.Id,1,"Stale")));
        var verified=await new UpdateJobVerificationCommandHandler(db,new FixedTimeProvider(Now.AddHours(2))).HandleAsync(new(job.Id,"VERIFIED",2));Assert.Equal(Now.AddHours(2),verified.VerifiedAt);await Assert.ThrowsAsync<ConflictException>(()=>new UpdateJobVerificationCommandHandler(db,new FixedTimeProvider(Now)).HandleAsync(new(job.Id,"PENDING",2)));var unverified=await new UpdateJobVerificationCommandHandler(db,new FixedTimeProvider(Now.AddHours(3))).HandleAsync(new(job.Id,"UNVERIFIED",3));Assert.Null(unverified.VerifiedAt);var selected=await new UpdateJobSelectionCommandHandler(db,new FixedTimeProvider(Now.AddHours(4))).HandleAsync(new(job.Id,"APPROVED",4));Assert.Equal("APPROVED",selected.SelectionStatus);await Assert.ThrowsAsync<ConflictException>(()=>new UpdateJobSelectionCommandHandler(db,new FixedTimeProvider(Now)).HandleAsync(new(job.Id,"SKIPPED",4)));var archived=await new ArchiveJobPostingCommandHandler(db,new FixedTimeProvider(Now.AddHours(5))).HandleAsync(new(job.Id,5));Assert.NotNull(archived.ArchivedAt);await Assert.ThrowsAsync<ConflictException>(()=>new ArchiveJobPostingCommandHandler(db,new FixedTimeProvider(Now)).HandleAsync(new(job.Id,6)));
    }

    [Fact]public async Task Application_create_and_edit_persist_immutable_created_event_and_versioning()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Company","Role",Now);db.JobPostings.Add(job);await db.SaveChangesAsync();var created=await new CreateJobApplicationCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(job.Id,"EMAIL","jobs@example.com",null,null,"note"));Assert.Equal("DRAFT",created.Status);var e=Assert.Single(created.Events);Assert.Equal("CREATED",e.EventType);Assert.Null(e.FromStatus);Assert.Equal("DRAFT",e.ToStatus);Assert.Equal(AdminId,e.ActorAdminUserId);var updated=await new UpdateJobApplicationCommandHandler(db,new FixedTimeProvider(Now.AddHours(1))).HandleAsync(new(created.Id,1,"PLATFORM",null,"https://example.com/apply","external","edited"));Assert.Equal(2,updated.Version);Assert.Equal("DRAFT",updated.Status);await Assert.ThrowsAsync<ConflictException>(()=>new UpdateJobApplicationCommandHandler(db,new FixedTimeProvider(Now)).HandleAsync(new(created.Id,1,null,null,null,null,null)));
    }

    [Theory]
    [InlineData("DRAFT","APPLIED")][InlineData("DRAFT","WITHDRAWN")][InlineData("APPLIED","INTERVIEW")][InlineData("APPLIED","REJECTED")][InlineData("APPLIED","WITHDRAWN")][InlineData("INTERVIEW","REJECTED")][InlineData("INTERVIEW","OFFER")][InlineData("INTERVIEW","WITHDRAWN")][InlineData("OFFER","WITHDRAWN")]
    public async Task Every_allowed_transition_updates_state_version_timestamps_and_history(string from,string to)
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Company","Role",Now);var app=Application(job.Id,from);db.AddRange(job,app);await db.SaveChangesAsync();var result=await new TransitionJobApplicationCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,to,1,"transition",null));Assert.Equal(to,result.Status);Assert.Equal(2,result.Version);Assert.Equal(Now,result.LastActivityAt);if(to=="APPLIED")Assert.Equal(Now,result.AppliedAt);var e=Assert.Single(result.Events);Assert.Equal((from,to,"STATUS_CHANGED"),(e.FromStatus,e.ToStatus,e.EventType));
    }

    [Theory][InlineData("DRAFT","INTERVIEW")][InlineData("APPLIED","OFFER")][InlineData("OFFER","REJECTED")][InlineData("REJECTED","APPLIED")][InlineData("WITHDRAWN","DRAFT")]
    public async Task Prohibited_and_terminal_transitions_fail_without_event(string from,string to)
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Company","Role",Now);var app=Application(job.Id,from);db.AddRange(job,app);await db.SaveChangesAsync();await Assert.ThrowsAsync<ConflictException>(()=>new TransitionJobApplicationCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,to,1,null,null)));Assert.Empty(await db.JobApplicationEvents.ToListAsync());Assert.Equal(from,app.Status);
    }

    [Fact]public async Task Documents_attach_and_soft_remove_with_events_and_repeat_conflict()
    {
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Company","Role",Now);var app=Application(job.Id,"DRAFT");db.AddRange(job,app);await db.SaveChangesAsync();using var metadata=JsonDocument.Parse("{\"kind\":\"cv\"}");var attached=await new AttachJobApplicationDocumentCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,"CV","v1","cv.pdf","private/cv.pdf",new string('a',64),metadata.RootElement));Assert.Equal("CV",attached.DocumentType);Assert.Equal("DOCUMENT_ATTACHED",(await db.JobApplicationEvents.SingleAsync()).EventType);await new RemoveJobApplicationDocumentCommandHandler(db,new FixedTimeProvider(Now.AddMinutes(1)),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,attached.Id));Assert.NotNull((await db.JobApplicationDocuments.SingleAsync()).RemovedAt);Assert.Equal(2,await db.JobApplicationEvents.CountAsync());Assert.True(await db.JobApplicationDocuments.AnyAsync(x=>x.Id==attached.Id));await Assert.ThrowsAsync<ConflictException>(()=>new RemoveJobApplicationDocumentCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,attached.Id)));
    }

    [Fact]public async Task Validators_reject_invalid_paging_values_urls_stack_versions_and_future_transition_time()
    {
        var q=await new GetJobPostingsQueryValidator().ValidateAsync(new(0,101,null,"bad",null,null,null));Assert.NotEmpty(q);using var objectJson=JsonDocument.Parse("{}");var create=Create("x","bad-url","raw") with{TechnologyStack=objectJson.RootElement};Assert.NotEmpty(await new CreateJobPostingCommandValidator().ValidateAsync(create));Assert.NotEmpty(await new UpdateJobApplicationCommandValidator().ValidateAsync(new(Guid.NewGuid(),0,"bad","bad","bad",new string('x',501),null)));
        await using var db=PublicPortfolioTests.CreateContext();var job=Posting("Company","Role",Now);var app=Application(job.Id,"DRAFT");db.AddRange(job,app);await db.SaveChangesAsync();await Assert.ThrowsAsync<ValidationException>(()=>new TransitionJobApplicationCommandHandler(db,new FixedTimeProvider(Now),new FakeCurrentUser(AdminId)).HandleAsync(new(app.Id,"APPLIED",1,null,Now.AddSeconds(1))));
    }

    private static CreateJobPostingCommand Create(string external,string url,string raw){using var json=JsonDocument.Parse("[\"C#\"]");return new("MANUAL",external,url,raw,"Acme","Developer","Hanoi",null,null,null,null,"USD",null,null,"Description",json.RootElement.Clone(),"jobs@example.com","https://example.com/apply",null,null);}
    private static UpdateJobPostingCommand Update(Guid id,int version,string company){using var json=JsonDocument.Parse("[\"C#\"]");return new(id,version,company,"Role","Hanoi",null,null,null,null,null,null,null,"Description",json.RootElement.Clone(),null,null,null,null);}
    private static JobPosting Posting(string company,string title,DateTimeOffset at,Guid? id=null){return new(){Id=id??Guid.NewGuid(),CompanyName=company,PositionTitle=title,Location="Hanoi",Description="Description",TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus="PENDING",SelectionStatus="PENDING_ANALYSIS",Version=1,CreatedAt=at,UpdatedAt=at};}
    private static RawJobPosting Raw(Guid jobId,string source)=>new(){Id=Guid.NewGuid(),JobPostingId=jobId,Source=source,RawContent="raw",ContentHash="hash",IngestionStatus="NORMALIZED",Metadata=JsonDocument.Parse("{}"),DiscoveredAt=Now,CreatedAt=Now,UpdatedAt=Now};
    private static JobApplication Application(Guid jobId,string status)=>new(){Id=Guid.NewGuid(),JobPostingId=jobId,Status=status,Version=1,CreatedAt=Now,UpdatedAt=Now};
}
