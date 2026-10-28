using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Real transaction/FK tests ONLY when an existing local disposable *_tests DB is
// explicitly configured. No application connection, provider or migration runner.
public sealed class DiscoveryExecutionResetPostgresTests
{
    [PostgresFact] public Task Concurrent_real_handler_claims_across_contexts_authorize_once()=>WithDatabase(async cs=>
    {
        var id=Guid.NewGuid();await using(var seed=Context(cs)){seed.DiscoveryExecutions.Add(new(){Id=id,ClientRequestId=Guid.NewGuid(),State="QUEUED"});await seed.SaveChangesAsync();}
        async Task<bool> Claim(){await using var context=Context(cs);try{await Service(context).ClaimAsync(id,"10002",default);return true;}catch(ConflictException){return false;}}
        var outcomes=await Task.WhenAll(Claim(),Claim());Assert.Single(outcomes.Where(x=>x));
        await using var verify=Context(cs);Assert.Single(await verify.DiscoveryExecutionAttempts.Where(x=>x.ExecutionId==id).ToListAsync());
        Assert.Single(await verify.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==id&&x.State=="CLAIMED").ToListAsync());
        Assert.Equal("CLAIMED",(await verify.DiscoveryExecutions.SingleAsync(x=>x.Id==id)).State);
        await Assert.ThrowsAsync<ConflictException>(()=>Service(verify).ClaimAsync(id,"10002",default));
    });

    [PostgresFact] public Task Database_claim_race_without_process_semaphore_rolls_back_loser_event()=>WithDatabase(async cs=>
    {
        var id=Guid.NewGuid();await using(var seed=Context(cs)){seed.DiscoveryExecutions.Add(new(){Id=id,ClientRequestId=Guid.NewGuid(),State="QUEUED"});await seed.SaveChangesAsync();}
        // Both contexts read the same version BEFORE either write. This exercises
        // actual PostgreSQL concurrency/uniqueness, not the service semaphore.
        await using var first=Context(cs);await using var second=Context(cs);
        var a=await first.DiscoveryExecutions.SingleAsync(x=>x.Id==id);var b=await second.DiscoveryExecutions.SingleAsync(x=>x.Id==id);
        async Task<bool> Save(ApplicationDbContext context,DiscoveryExecution e){e.State="CLAIMED";e.Version++;e.WorkflowRunId="10002";
            context.DiscoveryExecutionAttempts.Add(new(){Id=id,ExecutionId=id,WorkflowRunId="10002"});
            context.DiscoveryExecutionEvents.Add(new(){Id=Guid.NewGuid(),ExecutionId=id,State="CLAIMED"});
            try{await context.SaveChangesAsync();return true;}catch(DbUpdateException){return false;}}
        Assert.Single((await Task.WhenAll(Save(first,a),Save(second,b))).Where(x=>x));
        await using var verify=Context(cs);Assert.Single(await verify.DiscoveryExecutionAttempts.Where(x=>x.ExecutionId==id).ToListAsync());
        Assert.Single(await verify.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==id).ToListAsync());
    });

    [PostgresFact] public Task Concurrent_first_start_same_key_across_contexts_dispatches_only_once()=>WithDatabase(async cs=>
    {
        await PortfolioQueryFixture(cs);var key=Guid.NewGuid();var github=new CountDispatch();
        async Task Start(){await using var context=Context(cs);await Service(context,github).StartAsync(key,"ADMIN_MANUAL");}
        await Task.WhenAll(Start(),Start());Assert.Equal(1,github.Dispatches);
        await using var verify=Context(cs);var execution=await verify.DiscoveryExecutions.SingleAsync(x=>x.ClientRequestId==key);
        Assert.Equal("QUEUED",execution.State);Assert.Empty(await verify.DiscoveryExecutionAttempts.Where(x=>x.ExecutionId==execution.Id).ToListAsync());
        Assert.Single(await verify.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==execution.Id&&x.State=="DISPATCHING").ToListAsync());
        await Service(verify,github).StartAsync(key,"ADMIN_MANUAL");Assert.Equal(1,github.Dispatches);
    });

    [PostgresFact] public Task Database_same_key_insert_race_has_one_intent_and_no_orphan_audit()=>WithDatabase(async cs=>
    {
        var key=Guid.NewGuid();async Task<bool> Insert(){await using var context=Context(cs);var id=Guid.NewGuid();
            context.DiscoveryExecutions.Add(new(){Id=id,ClientRequestId=key,State="DISPATCHING"});context.DiscoveryExecutionEvents.Add(new(){Id=Guid.NewGuid(),ExecutionId=id,State="DISPATCHING"});
            try{await context.SaveChangesAsync();return true;}catch(DbUpdateException){return false;}}
        Assert.Single((await Task.WhenAll(Insert(),Insert())).Where(x=>x));await using var verify=Context(cs);
        var e=await verify.DiscoveryExecutions.SingleAsync(x=>x.ClientRequestId==key);
        Assert.Single(await verify.DiscoveryExecutionEvents.Where(x=>x.State=="DISPATCHING").ToListAsync());
        Assert.Empty(await verify.DiscoveryExecutionAttempts.Where(x=>x.ExecutionId==e.Id).ToListAsync());
    });

    private static DiscoveryExecutionService Service(ApplicationDbContext db,IDiscoveryGitHubClient? github=null)=>new(db,github??new NoDispatch(),new NoSemantic(),Options.Create(new DiscoveryExecutionOptions{Enabled=true,FreePlanConfirmed=true,NoCashConfirmed=true}),new Actor(),TimeProvider.System);
    private sealed class CountDispatch:IDiscoveryGitHubClient{public int Dispatches;public Task<string> DispatchAsync(Guid id,CancellationToken ct){Interlocked.Increment(ref Dispatches);return Task.FromResult("ACCEPTED");}public Task<bool> VerifyRunAsync(Guid id,string runId,CancellationToken ct)=>Task.FromResult(true);}
    private static async Task PortfolioQueryFixture(string cs)
    {
        // Query-only Portfolio projections, synthetic and isolated. Business FK
        // and concurrency tables above use the actual migration definitions.
        await using var sql=new NpgsqlConnection(cs);await sql.OpenAsync();await using var command=new NpgsqlCommand("""
            CREATE TABLE technologies(id uuid PRIMARY KEY,name text,category text,is_active boolean);
            CREATE TABLE skills(id uuid PRIMARY KEY,name text,technology_id uuid,experience_level text,is_published boolean);
            CREATE TABLE experiences(id uuid PRIMARY KEY,is_published boolean);
            CREATE TABLE experience_technologies(experience_id uuid,technology_id uuid,display_order int);
            CREATE TABLE projects(id uuid PRIMARY KEY,status text,is_published boolean);
            CREATE TABLE project_technologies(project_id uuid,technology_id uuid,display_order int);
            CREATE TABLE candidate_job_preferences(id uuid PRIMARY KEY,singleton_key text,target_roles jsonb,preferred_technologies jsonb,acceptable_locations jsonb,workplace_types jsonb,employment_types jsonb,minimum_salary numeric,salary_currency text,salary_period text,version int,created_at timestamptz,updated_at timestamptz);
            INSERT INTO technologies VALUES ('11111111-1111-1111-1111-111111111111','C#','BACKEND',true);
            INSERT INTO skills VALUES ('22222222-2222-2222-2222-222222222222','C#','11111111-1111-1111-1111-111111111111','USED',true);
            INSERT INTO candidate_job_preferences VALUES ('33333333-3333-3333-3333-333333333333','CURRENT','["Backend"]','["C#"]','["Hanoi"]','[]','[]',NULL,NULL,NULL,1,NOW(),NOW());
            """,sql);await command.ExecuteNonQueryAsync();
    }
    [PostgresFact] public Task Multi_vacancy_reset_keeps_claim_credits_history_and_replay_protection()=>WithDatabase(async cs=>
    {
        await using var db=Context(cs);var execution=await db.DiscoveryExecutions.AsNoTracking().SingleAsync();
        var attempt=JsonSerializer.Serialize(await db.DiscoveryExecutionAttempts.AsNoTracking().SingleAsync());
        var before=await Preview(db);Assert.Equal(2,before.Counts["discovery_execution_vacancies"]);
        var result=await new ResetAllJobDataHandler(db,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(db)).HandleAsync(new(JobDataReset.Confirmation,before.Fingerprint));
        Assert.Equal(0,result.Remaining.TotalRecords);
        await using var verify=Context(cs);
        Assert.Equal(JsonSerializer.Serialize(execution),JsonSerializer.Serialize(await verify.DiscoveryExecutions.AsNoTracking().SingleAsync()));
        Assert.Equal(attempt,JsonSerializer.Serialize(await verify.DiscoveryExecutionAttempts.AsNoTracking().SingleAsync()));
        Assert.Equal(1,await verify.DiscoveryExecutionEvents.CountAsync(e=>e.State=="SUCCEEDED"));
        Assert.Equal(1,await verify.DiscoveryExecutionEvents.CountAsync(e=>e.State=="RECRUITMENT_DATA_RESET"));
        Assert.Empty(await verify.DiscoveryExecutionObservations.ToListAsync());Assert.Empty(await verify.DiscoveryExecutionVacancies.ToListAsync());
        var service=new DiscoveryExecutionService(verify,new NoDispatch(),new NoSemantic(),Options.Create(new DiscoveryExecutionOptions{Enabled=true,FreePlanConfirmed=true,NoCashConfirmed=true}),new Actor(),TimeProvider.System);
        await service.StartAsync(execution.ClientRequestId,"ADMIN_MANUAL"); // Existing key must not dispatch again.
        await Assert.ThrowsAsync<ConflictException>(()=>service.ClaimAsync(execution.Id,"10001",default));
        var empty=await Preview(verify);
        await new ResetAllJobDataHandler(verify,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(verify)).HandleAsync(new(JobDataReset.Confirmation,empty.Fingerprint));
        Assert.Equal(1,await verify.DiscoveryExecutionEvents.CountAsync(e=>e.State=="RECRUITMENT_DATA_RESET"));
        await using var sql=new NpgsqlConnection(cs);await sql.OpenAsync();
        await using var keep=new NpgsqlCommand("SELECT count(*) FROM unrelated_portfolio",sql);Assert.Equal(1L,await keep.ExecuteScalarAsync());
    });

    [PostgresFact] public Task Commit_failure_rolls_back_real_deletes_and_reset_audit()=>WithDatabase(async cs=>
    {
        await using var db=Context(cs);var before=await Preview(db);var failing=new FailCommit(new NpgsqlDiscoveryCleanupTransactionFactory(db));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new ResetAllJobDataHandler(db,new Actor(),failing).HandleAsync(new(JobDataReset.Confirmation,before.Fingerprint)));
        Assert.True(failing.Deleted);
        await using var verify=Context(cs);Assert.Equal(before.Fingerprint,(await Preview(verify)).Fingerprint);
        Assert.Equal(2,await verify.DiscoveryExecutionVacancies.CountAsync());Assert.Single(await verify.DiscoveryExecutionObservations.ToListAsync());
        Assert.DoesNotContain(await verify.DiscoveryExecutionEvents.ToListAsync(),e=>e.State=="RECRUITMENT_DATA_RESET");
        Assert.Equal(1m,(await verify.DiscoveryExecutionAttempts.SingleAsync()).CreditsDeducted);
    });

    [PostgresFact] public Task Active_execution_rejects_preview_and_execute_preserving_every_row()=>WithDatabase(async cs=>
    {
        await using var db=Context(cs);var plan=await Preview(db);var e=await db.DiscoveryExecutions.SingleAsync();e.ActiveKey="DISCOVERY";e.State="CLAIMED";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(()=>Preview(db));
        await Assert.ThrowsAsync<ConflictException>(()=>new ResetAllJobDataHandler(db,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(db)).HandleAsync(new(JobDataReset.Confirmation,plan.Fingerprint)));
        Assert.Equal(2,await db.DiscoveryExecutionVacancies.CountAsync());Assert.Single(await db.DiscoveryExecutionAttempts.ToListAsync());
    });

    [PostgresFact] public Task Reset_lock_prevents_new_execution_insert_until_transaction_is_disposed()=>WithDatabase(async cs=>
    {
        await using var db=Context(cs);await using var tx=await new NpgsqlDiscoveryCleanupTransactionFactory(db).BeginResetAllAsync();
        await using var connection=new NpgsqlConnection(cs);await connection.OpenAsync();
        await using(var timeout=new NpgsqlCommand("SET lock_timeout = '250ms'",connection))await timeout.ExecuteNonQueryAsync();
        // Even an insert which would later fail NOT NULL checks must first obtain
        // its table lock. 55P03 proves it was blocked by reset, not by a fake lease.
        await using var insert=new NpgsqlCommand("INSERT INTO discovery_executions (\"Id\") VALUES (@id)",connection);insert.Parameters.AddWithValue("id",Guid.NewGuid());
        var error=await Assert.ThrowsAsync<PostgresException>(()=>insert.ExecuteNonQueryAsync());Assert.Equal("55P03",error.SqlState);
    });

    private static Task<JobDataResetPlan> Preview(ApplicationDbContext db)=>new GetJobDataResetPlanHandler(db,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(db)).HandleAsync(new());
    private static ApplicationDbContext Context(string cs)=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs,o=>o.UseVector()).Options);
    private static async Task WithDatabase(Func<string,Task> test)
    {
        var builder=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CHAT_QUOTA_TEST_CONNECTION")??throw new InvalidOperationException("Local test connection required."));
        if(builder.Host is not("localhost" or "127.0.0.1")||builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true)throw new InvalidOperationException("Only local *_tests database permitted.");
        var schema="execution_reset_"+Guid.NewGuid().ToString("N");builder.SearchPath=schema;var cs=builder.ConnectionString;
        await using var sql=new NpgsqlConnection(cs);await sql.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",sql))await create.ExecuteNonQueryAsync();
            // Minimal synthetic old-scope tables suffice for real Count/Delete/FK
            // checks. No pgvector extension or unrelated production schema needed.
            await using(var create=new NpgsqlCommand("""
                CREATE TABLE job_postings(id uuid PRIMARY KEY);
                CREATE TABLE raw_job_postings(id uuid PRIMARY KEY,job_posting_id uuid REFERENCES job_postings(id),duplicate_of_raw_job_posting_id uuid REFERENCES raw_job_postings(id));
                CREATE TABLE job_applications(id uuid PRIMARY KEY,job_posting_id uuid REFERENCES job_postings(id));
                CREATE TABLE submission_attempts(id uuid PRIMARY KEY,job_application_id uuid REFERENCES job_applications(id));
                CREATE TABLE submission_attempt_events(id uuid PRIMARY KEY,submission_attempt_id uuid REFERENCES submission_attempts(id));
                CREATE TABLE job_application_documents(id uuid PRIMARY KEY,job_application_id uuid REFERENCES job_applications(id));
                CREATE TABLE job_application_events(id uuid PRIMARY KEY,job_application_id uuid REFERENCES job_applications(id));
                CREATE TABLE raw_job_posting_attachments(id uuid PRIMARY KEY,raw_job_posting_id uuid REFERENCES raw_job_postings(id));
                CREATE TABLE discovery_candidates(id uuid PRIMARY KEY);
                CREATE TABLE discovery_candidate_reviews(id uuid PRIMARY KEY,candidate_id uuid REFERENCES discovery_candidates(id));
                CREATE TABLE discovery_candidate_review_events(id uuid PRIMARY KEY,candidate_id uuid REFERENCES discovery_candidates(id));
                CREATE TABLE unrelated_portfolio(id int PRIMARY KEY);
                INSERT INTO unrelated_portfolio VALUES (1);
                """,sql))await create.ExecuteNonQueryAsync();
            await using var db=Context(cs);
            // Generate only the five new table definitions in this isolated test
            // schema, without EF Migrate/History or any application database.
            var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[DiscoveryReleaseMigration.Executions],db.Database.ProviderName!);
            foreach(var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
            {await using var ddl=new NpgsqlCommand(command.CommandText,sql);await ddl.ExecuteNonQueryAsync();}
            var raw=Guid.NewGuid();var job=Guid.NewGuid();var secondJob=Guid.NewGuid();
            await using(var seed=new NpgsqlCommand("INSERT INTO job_postings(id) VALUES (@job),(@second); INSERT INTO raw_job_postings(id,job_posting_id) VALUES (@raw,@job)",sql))
            {seed.Parameters.AddWithValue("job",job);seed.Parameters.AddWithValue("second",secondJob);seed.Parameters.AddWithValue("raw",raw);await seed.ExecuteNonQueryAsync();}
            var execution=Guid.NewGuid();var observation=Guid.NewGuid();
            db.DiscoveryExecutions.Add(new(){Id=execution,ClientRequestId=Guid.NewGuid(),State="SUCCEEDED",ActiveKey=null,WorkflowRunId="10001",ResultHash=new string('c',64),Version=7});
            db.DiscoveryExecutionAttempts.Add(new(){Id=execution,ExecutionId=execution,WorkflowRunId="10001",Outcome="SUCCEEDED",ActualRequests=1,CreditsDeducted=1,CreditsUsedAlternateHeader=6,HttpStatus=200});
            db.DiscoveryExecutionEvents.Add(new(){Id=Guid.NewGuid(),ExecutionId=execution,State="SUCCEEDED"});
            db.DiscoveryExecutionObservations.Add(new(){Id=observation,ExecutionId=execution,ItemIndex=0,RawJobPostingId=raw,PayloadHash=new string('a',64),ContentHash=new string('a',64)});
            foreach(var (i,id) in new[]{job,secondJob}.Select((id,i)=>(i,id)))db.DiscoveryExecutionVacancies.Add(new(){Id=Guid.NewGuid(),ObservationId=observation,VacancyIndex=i,RawJobPostingId=raw,JobPostingId=id,Fingerprint=new string('a',64)});
            await db.SaveChangesAsync();await test(cs);
        }
        finally{await using var cleanup=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",sql);await cleanup.ExecuteNonQueryAsync();}
    }
    private sealed class Actor:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>Guid.Parse("11111111-1111-1111-1111-111111111111");public string? Email=>null;}
    private sealed class NoDispatch:IDiscoveryGitHubClient
    {public Task<string> DispatchAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("No dispatch permitted");public Task<bool> VerifyRunAsync(Guid id,string runId,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class NoSemantic:IDiscoveryTextExtractor
    {public string ModelIdentity=>"synthetic";public Task<DiscoveryTextGeneration> ExtractAsync(string raw,CancellationToken ct)=>throw new InvalidOperationException("No provider permitted");}
    private sealed class FailCommit(IDiscoveryCleanupTransactionFactory inner):IDiscoveryCleanupTransactionFactory
    {
        public bool Deleted;
        public Task<IDiscoveryCleanupTransaction> BeginAsync(CancellationToken ct=default)=>inner.BeginAsync(ct);
        public async Task<IJobDataResetTransaction> BeginResetAllAsync(CancellationToken ct=default)=>new Fail(await inner.BeginResetAllAsync(ct),this);
        private sealed class Fail(IJobDataResetTransaction inner,FailCommit owner):IJobDataResetTransaction
        {
            public async Task DeleteAllJobDataAsync(CancellationToken ct=default){await inner.DeleteAllJobDataAsync(ct);owner.Deleted=true;}
            public Task CommitAsync(CancellationToken ct=default)=>throw new InvalidOperationException("Synthetic commit failure");
            public ValueTask DisposeAsync()=>inner.DisposeAsync();
        }
    }
}
