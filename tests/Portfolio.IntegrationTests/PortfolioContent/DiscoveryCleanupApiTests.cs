using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.IntegrationTests.Authentication;
using Portfolio.UnitTests.PortfolioContent;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryCleanupApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private const string FormerRun = "fresh-demo-20261027-a2";
    private const string Route = "/api/v1/admin/job-hunting/discovery";
    [Fact] public async Task Cleanup_requires_Admin_not_anonymous_or_import_only_credentials()
    {
        using var f = Factory(); using var client = f.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        foreach(var machine in new[]{false,true})
        {
            if(machine)client.DefaultRequestHeaders.Add("X-Discovery-Key",new string('a',64));
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(Route+"/cleanup/runs")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(Route+"/cleanup/reset-all/preview")).StatusCode);
            foreach(var path in new[]{"demo-reset-plan","cleanup/backup","cleanup/execute","cleanup/reset-all/execute"})
                Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync(Route+"/"+path,new{})).StatusCode);
        }
    }
    [Fact] public async Task Actual_run_list_explicit_scope_backup_delete_and_restore_preserve_every_field_and_other_entities()
    {
        using var f=Factory();using var client=Admin(f);await Seed(client);
        var jobId=Guid.NewGuid();var applicationId=Guid.NewGuid();
        using(var existing=f.Services.CreateScope())
        {
            var data=existing.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            data.JobPostings.Add(new JobPosting{Id=jobId,CompanyName="Keep company",PositionTitle="Keep job",Location="Hanoi",Description="Unrelated data",
                TechnologyStack=JsonDocument.Parse("[]"),Version=7});
            data.JobApplications.Add(new JobApplication{Id=applicationId,JobPostingId=jobId,Notes="Keep application",Version=3});
            await data.SaveChangesAsync();
        }
        var runs=await Data<DiscoveryRunSummary[]>(await client.GetAsync(Route+"/cleanup/runs"));
        Assert.Equal(new[]{FormerRun,"legacy-a","legacy-b"}.Order(),runs.Select(x=>x.RunId).Order());
        Assert.All(runs,r=>Assert.False(r.Protected));
        var plan=await Data<DiscoveryDemoResetPlan>(await client.PostAsJsonAsync(Route+"/demo-reset-plan",new{runIds=new[]{"legacy-a"}}));
        var backup=await Backup(client);Assert.Equal(plan.Fingerprint,backup.Fingerprint);
        Assert.Single(backup.Snapshot.Candidates);Assert.Single(backup.Snapshot.Reviews);Assert.Single(backup.Snapshot.ReviewEvents);
        var disk=JsonSerializer.Deserialize<DiscoveryCleanupBackup>(JsonSerializer.Serialize(backup,DiscoveryReviewRules.Json),DiscoveryReviewRules.Json)!;
        var result=await Data<DiscoveryCleanupResult>(await client.PostAsJsonAsync(Route+"/cleanup/execute",Command(disk)));
        Assert.Equal(new(1,1,1),result);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(Route+"/cleanup/execute",Command(disk))).StatusCode);
        using var scope=f.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Equal(2,await db.DiscoveryCandidates.CountAsync());Assert.Empty(await db.DiscoveryCandidateReviews.ToListAsync());
        Assert.Empty(await db.DiscoveryCandidateReviewEvents.ToListAsync());
        var keptJob=await db.JobPostings.SingleAsync();Assert.Equal(jobId,keptJob.Id);Assert.Equal(7,keptJob.Version);Assert.Equal("Keep job",keptJob.PositionTitle);
        var keptApplication=await db.JobApplications.SingleAsync();Assert.Equal(applicationId,keptApplication.Id);Assert.Equal(3,keptApplication.Version);Assert.Equal("Keep application",keptApplication.Notes);
        Assert.Empty(await db.RawJobPostings.ToListAsync());Assert.Empty(await db.SubmissionAttempts.ToListAsync());
        Assert.Contains(await db.DiscoveryCandidates.ToListAsync(),c=>c.RunId==FormerRun);
        // Offline restore rehearsal: preserve identities/timestamps/payload/human decisions,
        // not a classifier reimport which would lose the original human audit history.
        db.DiscoveryCandidates.AddRange(disk.Snapshot.Candidates);await db.SaveChangesAsync();
        db.DiscoveryCandidateReviews.AddRange(disk.Snapshot.Reviews);await db.SaveChangesAsync();
        db.DiscoveryCandidateReviewEvents.AddRange(disk.Snapshot.ReviewEvents);await db.SaveChangesAsync();
        Assert.Equal(backup.Fingerprint,DiscoveryCleanup.Hash(await DiscoveryCleanup.ReadAsync(db,["legacy-a"],default)));
    }
    [Fact] public async Task Empty_wildcard_duplicate_scope_and_unconfirmed_or_incomplete_backup_fail_closed()
    {
        using var f=Factory();using var client=Admin(f);await Seed(client);var backup=await Backup(client);
        foreach(var ids in new[]{Array.Empty<string>(),new[]{"*"},new[]{"legacy-a","legacy-a"}})
        {
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Route+"/cleanup/backup",new{runIds=ids})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Route+"/cleanup/execute",Command(backup) with{RunIds=ids})).StatusCode);
        }
        foreach(var command in new[]{Command(backup) with{BackupSaved=false},Command(backup) with{Confirmation="delete"},
            Command(backup) with{Backup=backup with{Snapshot=backup.Snapshot with{Reviews=[]}}}})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Route+"/cleanup/execute",command)).StatusCode);
        Assert.Equal(3,(await Data<DiscoveryRunSummary[]>(await client.GetAsync(Route+"/cleanup/runs"))).Length);
    }
    [Fact] public async Task Review_change_after_backup_is_stale_and_concurrent_deletes_cannot_both_succeed()
    {
        using var f=Factory();using var client=Admin(f);await Seed(client);var old=await Backup(client);
        var id=old.Snapshot.Candidates.Single().Id;
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"{Route}/{id}/reviews",new{expectedVersion=1,action="UNCERTAIN",notes="Changed after backup"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(Route+"/cleanup/execute",Command(old))).StatusCode);
        var fresh=await Backup(client);var results=await Task.WhenAll(client.PostAsJsonAsync(Route+"/cleanup/execute",Command(fresh)),client.PostAsJsonAsync(Route+"/cleanup/execute",Command(fresh)));
        Assert.Single(results.Where(r=>r.StatusCode==HttpStatusCode.OK));Assert.Single(results.Where(r=>r.StatusCode==HttpStatusCode.Conflict));
    }
    [Fact] public async Task Save_failure_does_not_delete_any_records_in_the_inmemory_contract_test()
    {
        var fault=new Fault();using var f=Factory(fault);using var client=Admin(f);await Seed(client);var backup=await Backup(client);
        fault.FailSave=true;
        Assert.Equal(HttpStatusCode.InternalServerError,(await client.PostAsJsonAsync(Route+"/cleanup/execute",Command(backup))).StatusCode);
        fault.FailSave=false;var after=await Backup(client);Assert.Equal(backup.Fingerprint,after.Fingerprint);
    }
    [Fact] public async Task Reset_all_clears_all_eleven_tables_including_former_protected_run_and_preserves_portfolio_and_cv()
    {
        using var f=Factory();using var client=Admin(f);await SeedAll(f,client);
        var before=await ResetPlan(client);Assert.Equal(11,before.Counts.Count);Assert.All(before.Counts.Values,n=>Assert.True(n>0));
        var result=await Data<JobDataResetResult>(await client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",new ResetAllJobDataCommand(JobDataReset.Confirmation,before.Fingerprint)));
        Assert.Equal(before.TotalRecords,result.Deleted.TotalRecords);Assert.Equal(0,result.Remaining.TotalRecords);Assert.All(result.Remaining.Counts.Values,n=>Assert.Equal(0,n));
        Assert.Empty(await Data<DiscoveryRunSummary[]>(await client.GetAsync(Route+"/cleanup/runs")));
        var jobs=await Data<JsonElement>(await client.GetAsync("/api/v1/admin/job-postings"));Assert.Equal(0,jobs.GetProperty("total").GetInt32());
        var discovery=await Data<JsonElement>(await client.GetAsync(Route));Assert.Equal(0,discovery.GetProperty("results").GetProperty("total").GetInt32());
        using var scope=f.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Equal("Keep portfolio",(await db.Profiles.SingleAsync()).FullName);Assert.Equal("Keep project",(await db.Projects.SingleAsync()).Title);
        Assert.Equal("Keep skill",(await db.Skills.SingleAsync()).Name);Assert.Equal("Keep technology",(await db.Technologies.SingleAsync()).Name);
        Assert.Equal("private/canonical.pdf",(await db.CanonicalCvs.SingleAsync()).StorageKey);
        var empty=await ResetPlan(client);Assert.Equal(0,empty.TotalRecords);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",new ResetAllJobDataCommand(JobDataReset.Confirmation,empty.Fingerprint))).StatusCode);
    }
    [Fact] public async Task Reset_all_requires_exact_confirmation_and_rejects_stale_preview_without_deleting()
    {
        using var f=Factory();using var client=Admin(f);await Seed(client);var plan=await ResetPlan(client);
        foreach(var text in new[]{"", "reset all job data", "RESET ALL JOB DATA "})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",new ResetAllJobDataCommand(text,plan.Fingerprint))).StatusCode);
        using(var scope=f.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.DiscoveryCandidates.Add(new(){Id=Guid.NewGuid(),RunId="new-after-preview",Query="test",ContentStatus="NEEDS_REVIEW",TargetStatus="INSUFFICIENT_DATA",Payload="{}",PayloadHash=new string('b',64)});
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",new ResetAllJobDataCommand(JobDataReset.Confirmation,plan.Fingerprint))).StatusCode);
        Assert.Equal(4,(await Data<DiscoveryRunSummary[]>(await client.GetAsync(Route+"/cleanup/runs"))).Length);
    }
    [Fact] public async Task Concurrent_reset_all_has_one_winner_and_save_failure_keeps_all_test_data()
    {
        var fault=new Fault();using var f=Factory(fault);using var client=Admin(f);await SeedAll(f,client);var before=await ResetPlan(client);
        var command=new ResetAllJobDataCommand(JobDataReset.Confirmation,before.Fingerprint);
        fault.FailSave=true;Assert.Equal(HttpStatusCode.InternalServerError,(await client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",command)).StatusCode);
        fault.FailSave=false;Assert.Equal(before.Fingerprint,(await ResetPlan(client)).Fingerprint);
        var results=await Task.WhenAll(client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",command),client.PostAsJsonAsync(Route+"/cleanup/reset-all/execute",command));
        Assert.Single(results.Where(r=>r.StatusCode==HttpStatusCode.OK));Assert.Single(results.Where(r=>r.StatusCode==HttpStatusCode.Conflict));
        Assert.Equal(0,(await ResetPlan(client)).TotalRecords);
    }
    private static async Task<JobDataResetPlan> ResetPlan(HttpClient c)=>await Data<JobDataResetPlan>(await c.GetAsync(Route+"/cleanup/reset-all/preview"));
    private static async Task SeedAll(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f,HttpClient client)
    {
        await Seed(client);using var scope=f.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var job=Guid.NewGuid();var raw=Guid.NewGuid();var duplicate=Guid.NewGuid();var app=Guid.NewGuid();var attempt=Guid.NewGuid();
        db.JobPostings.Add(new(){Id=job,CompanyName="Synthetic",PositionTitle="Backend",Location="Hanoi",Description="JD",TechnologyStack=JsonDocument.Parse("[]")});
        db.RawJobPostings.Add(new(){Id=raw,JobPostingId=job,RawContent="JD",ContentHash=new string('a',64),Metadata=JsonDocument.Parse("{}")});
        db.RawJobPostings.Add(new(){Id=duplicate,JobPostingId=job,DuplicateOfRawJobPostingId=raw,RawContent="JD duplicate",ContentHash=new string('b',64),Metadata=JsonDocument.Parse("{}")});
        db.RawJobPostingAttachments.Add(new(){Id=Guid.NewGuid(),RawJobPostingId=raw,StorageKey="private/screenshot.png",ContentType="image/png",ContentHash=new string('a',64),FileSizeBytes=10});
        db.JobApplications.Add(new(){Id=app,JobPostingId=job});
        db.JobApplicationDocuments.Add(new(){Id=Guid.NewGuid(),JobApplicationId=app,DocumentType="CV",VersionLabel="v1",StorageKey="private/snapshot.pdf",Metadata=JsonDocument.Parse("{}")});
        db.JobApplicationEvents.Add(new(){Id=Guid.NewGuid(),JobApplicationId=app,Metadata=JsonDocument.Parse("{}")});
        db.SubmissionAttempts.Add(new(){Id=attempt,JobApplicationId=app,Provider="MANUAL",Status="CREATED",IdempotencyKey="synthetic-reset",PackageManifestHash=new string('a',64),CreatedByAdminUserId=AuthApiFactory.AdminId});
        db.SubmissionAttemptEvents.Add(new(){Id=Guid.NewGuid(),SubmissionAttemptId=attempt,ToStatus="CREATED",ActorAdminUserId=AuthApiFactory.AdminId});
        db.Profiles.Add(new(){Id=Guid.NewGuid(),FullName="Keep portfolio"});
        db.Projects.Add(new(){Id=Guid.NewGuid(),Slug="keep-project",Title="Keep project",Status="COMPLETED"});
        db.Skills.Add(new(){Id=Guid.NewGuid(),Name="Keep skill",Category="BACKEND",ExperienceLevel="USED"});
        db.Technologies.Add(new(){Id=Guid.NewGuid(),Name="Keep technology",Category="BACKEND"});
        db.CanonicalCvs.Add(new(){Id=Guid.NewGuid(),StorageKey="private/canonical.pdf",FileName="cv.pdf",ContentType="application/pdf",ContentHash=new string('a',64),FileSizeBytes=10});
        await db.SaveChangesAsync();
    }
    private static DeleteDiscoveryRunsCommand Command(DiscoveryCleanupBackup b)=>new(["legacy-a"],b.Fingerprint,b,true,DiscoveryCleanup.Confirmation);
    private static async Task<DiscoveryCleanupBackup> Backup(HttpClient c)=>await Data<DiscoveryCleanupBackup>(await c.PostAsJsonAsync(Route+"/cleanup/backup",new{runIds=new[]{"legacy-a"}}));
    private static async Task<T> Data<T>(HttpResponseMessage response)
    {Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Deserialize<T>(DiscoveryReviewRules.Json)!;}
    private static async Task Seed(HttpClient c)
    {
        foreach(var run in new[]{"legacy-a","legacy-b",FormerRun})
        {
            const string text="Hiring Fresher Backend Developer in Hanoi";
            var b=new DiscoveryReviewImport(1,"SOCIALAPIS",run,"Backend Hanoi",1,[new(0,"123","https://facebook.com/fixture/posts/123",text,
                DiscoveryReviewRules.Hash(text),DiscoveryReviewRules.Hash(text),null,new("VALID_RECRUITMENT",["EMPLOYER_HIRING"],"Synthetic.",[]),
                new("INSUFFICIENT_DATA",["PROFILE_NOT_PROVIDED"],"Synthetic.",[]),"NOT_CHECKED",JsonSerializer.SerializeToElement(new{company=(string?)null}),[],[],"content-first-v1",DateTimeOffset.Parse("2026-10-27T00:00:00Z"))]);
            Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync(Route+"/import",b)).StatusCode);
        }
        var list=await Data<JsonElement>(await c.GetAsync(Route+"?runId=legacy-a"));var id=list.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync($"{Route}/{id}/reviews",new{expectedVersion=0,action="CONFIRMED",notes="Preserve original audit"})).StatusCode);
    }
    private HttpClient Admin(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f)
    {
        var client=f.CreateClient(new(){BaseAddress=new Uri("https://localhost")});using var s=f.Services.CreateScope();
        var token=s.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId,"admin@example.com");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.Value);return client;
    }
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory(Fault? fault=null)
    {
        var database=Guid.NewGuid().ToString();var lease=new SemaphoreSlim(1);fault??=new();
        return root.WithWebHostBuilder(b=>{b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
            ["DiscoveryImport:Enabled"]="true",["DiscoveryImport:Key"]=new string('a',64)}));
            b.ConfigureServices(s=>{s.RemoveAll<IApplicationDbContext>();s.AddScoped<IApplicationDbContext>(_=>new ContentTestDbContext(new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(database).Options));
                s.RemoveAll<IDiscoveryCleanupTransactionFactory>();s.AddScoped<IDiscoveryCleanupTransactionFactory>(sp=>new MemorySerializedFactory((ContentTestDbContext)sp.GetRequiredService<IApplicationDbContext>(),lease,fault));});});
    }
    private sealed class Fault{public bool FailSave;}
    // This seam tests HTTP/application composition only, NOT PostgreSQL locks or rollback.
    private sealed class MemorySerializedFactory(ContentTestDbContext db,SemaphoreSlim lease,Fault fault):IDiscoveryCleanupTransactionFactory
    {
        public async Task<IDiscoveryCleanupTransaction> BeginAsync(CancellationToken ct=default){await lease.WaitAsync(ct);db.FailSaveChanges=fault.FailSave;return new Lease(lease);}
        public async Task<IJobDataResetTransaction> BeginResetAllAsync(CancellationToken ct=default){await lease.WaitAsync(ct);db.FailSaveChanges=fault.FailSave;return new ResetLease(db,lease);}
        private sealed class ResetLease(ContentTestDbContext db,SemaphoreSlim lease):IJobDataResetTransaction
        {
            public async Task DeleteAllJobDataAsync(CancellationToken ct=default)
            {
                db.DiscoveryCandidateReviewEvents.RemoveRange(await db.DiscoveryCandidateReviewEvents.ToListAsync(ct));
                db.DiscoveryCandidateReviews.RemoveRange(await db.DiscoveryCandidateReviews.ToListAsync(ct));
                db.DiscoveryCandidates.RemoveRange(await db.DiscoveryCandidates.ToListAsync(ct));
                db.SubmissionAttemptEvents.RemoveRange(await db.SubmissionAttemptEvents.ToListAsync(ct));
                db.SubmissionAttempts.RemoveRange(await db.SubmissionAttempts.ToListAsync(ct));
                db.JobApplicationDocuments.RemoveRange(await db.JobApplicationDocuments.ToListAsync(ct));
                db.JobApplicationEvents.RemoveRange(await db.JobApplicationEvents.ToListAsync(ct));
                db.JobApplications.RemoveRange(await db.JobApplications.ToListAsync(ct));
                db.RawJobPostingAttachments.RemoveRange(await db.RawJobPostingAttachments.ToListAsync(ct));
                var raw=await db.RawJobPostings.ToListAsync(ct);
                foreach(var r in raw){r.DuplicateOfRawJobPostingId=null;r.DuplicateOfRawJobPosting=null;}
                db.RawJobPostings.RemoveRange(raw);db.JobPostings.RemoveRange(await db.JobPostings.ToListAsync(ct));
                await db.SaveChangesAsync(ct);
            }
            public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;
            public ValueTask DisposeAsync(){lease.Release();return ValueTask.CompletedTask;}
        }
        private sealed class Lease(SemaphoreSlim lease):IDiscoveryCleanupTransaction{public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public ValueTask DisposeAsync(){lease.Release();return ValueTask.CompletedTask;}}
    }
}
