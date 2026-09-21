using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class JobApplicationUniquenessPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 4, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task Parent_lock_serializes_creation_before_archive()
    {
        var schema = Schema();
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            var jobPostingId = Guid.NewGuid();
            await SeedAsync(connectionString, jobPostingId);
            await using var createDb = Context(connectionString);
            var lockFactory = new PausingTransactionFactory(new NpgsqlJobApplicationCreationTransactionFactory(createDb));
            var createTask = Handler(createDb, lockFactory).HandleAsync(new(jobPostingId, 5));
            await lockFactory.LockAcquired.WaitAsync(TimeSpan.FromSeconds(10));

            var updateStarted = new UpdateStartedInterceptor();
            await using var archiveDb = Context(connectionString, updateStarted);
            var archiveTask = new ArchiveJobPostingCommandHandler(archiveDb, new FixedTimeProvider()).HandleAsync(new(jobPostingId, 5));
            var archiveProcessId = await updateStarted.ProcessId.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilBlockedAsync(Connection(), archiveProcessId);
            Assert.False(archiveTask.IsCompleted);

            lockFactory.Release();
            var created = await createTask.WaitAsync(TimeSpan.FromSeconds(10));
            var archived = await archiveTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(JobApplicationStatuses.Draft, created.Status);
            Assert.NotNull(archived.ArchivedAt);
            Assert.Equal(6, archived.Version);
            await using var verify = Context(connectionString);
            Assert.Single(await verify.JobApplications.AsNoTracking().ToListAsync());
            Assert.Single(await verify.JobApplicationEvents.AsNoTracking().ToListAsync());
        }
        finally { await DropAsync(Connection(), schema); }
    }

    [PostgresFact]
    public async Task Committed_parent_update_makes_the_expected_version_stale()
    {
        var schema = Schema();
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            var jobPostingId = Guid.NewGuid();
            await SeedAsync(connectionString, jobPostingId);
            await using (var archiveDb = Context(connectionString))
                await new ArchiveJobPostingCommandHandler(archiveDb, new FixedTimeProvider()).HandleAsync(new(jobPostingId, 5));

            await using var createDb = Context(connectionString);
            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Handler(createDb, new NpgsqlJobApplicationCreationTransactionFactory(createDb)).HandleAsync(new(jobPostingId, 5)));
            Assert.Equal("JOB_POSTING_VERSION_CONFLICT", error.Code);
            Assert.Empty(await createDb.JobApplications.AsNoTracking().ToListAsync());
            Assert.Empty(await createDb.JobApplicationEvents.AsNoTracking().ToListAsync());
        }
        finally { await DropAsync(Connection(), schema); }
    }

    [PostgresFact]
    public async Task Concurrent_real_handlers_create_exactly_one_application()
    {
        var schema = Schema();
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            var jobPostingId = Guid.NewGuid();
            await SeedAsync(connectionString, jobPostingId);
            var attempts = await Task.WhenAll(AttemptCreateAsync(connectionString, jobPostingId), AttemptCreateAsync(connectionString, jobPostingId));
            Assert.Single(attempts, attempt => attempt.Result is not null);
            var rejection = Assert.Single(attempts, attempt => attempt.Error is not null).Error!;
            Assert.Equal("JOB_APPLICATION_ALREADY_EXISTS", rejection.Code);
            await using var verify = Context(connectionString);
            Assert.Single(await verify.JobApplications.AsNoTracking().ToListAsync());
            Assert.Single(await verify.JobApplicationEvents.AsNoTracking().ToListAsync());
            verify.JobApplications.Add(new JobApplication
            {
                Id=Guid.NewGuid(),JobPostingId=jobPostingId,Status=JobApplicationStatuses.Draft,
                Version=1,CreatedAt=Now,UpdatedAt=Now,
            });
            var uniqueFailure=await Assert.ThrowsAsync<DbUpdateException>(()=>verify.SaveChangesAsync());
            Assert.True(new NpgsqlJobApplicationConflictDetector().IsDuplicateJobPosting(uniqueFailure));
        }
        finally { await DropAsync(Connection(), schema); }
    }

    private static async Task<(JobApplicationResult? Result, ConflictException? Error)> AttemptCreateAsync(string connectionString, Guid jobPostingId)
    {
        await using var db = Context(connectionString);
        try { return (await Handler(db, new NpgsqlJobApplicationCreationTransactionFactory(db)).HandleAsync(new(jobPostingId, 5)), null); }
        catch (ConflictException exception) { return (null, exception); }
    }

    private static CreateJobApplicationCommandHandler Handler(ApplicationDbContext db, IJobApplicationCreationTransactionFactory transactionFactory) =>
        new(db, new FixedTimeProvider(), new CurrentUser(), new NpgsqlJobApplicationConflictDetector(), transactionFactory);

    private static ApplicationDbContext Context(string connectionString, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString, options => options.UseVector()).AddInterceptors(interceptors).Options);

    private static async Task SeedAsync(string connectionString, Guid jobPostingId)
    {
        await using var db = Context(connectionString);
        db.JobPostings.Add(new JobPosting
        {
            Id=jobPostingId,CompanyName="Acme",PositionTitle="Developer",Location="Hanoi",Description="Description",
            TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus=JobPostingVerificationStatuses.Verified,
            SelectionStatus=JobPostingSelectionStatuses.Approved,ApplicationEmail="jobs@example.com",
            ApplicationUrl="https://example.com/apply",Version=5,CreatedAt=Now,UpdatedAt=Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task CreateAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SCHEMA "{{schema}}";
            CREATE TABLE job_postings (
              id uuid PRIMARY KEY, company_name varchar(255) NOT NULL, position_title varchar(255) NOT NULL, location varchar(255) NOT NULL,
              employment_type varchar(50) NULL, workplace_type varchar(50) NULL, salary_minimum numeric(18,2) NULL, salary_maximum numeric(18,2) NULL,
              salary_currency varchar(3) NULL, salary_period varchar(30) NULL, experience_requirements text NULL, description text NOT NULL,
              technology_stack jsonb NOT NULL DEFAULT '[]'::jsonb, application_email varchar(255) NULL, application_url text NULL,
              verification_status varchar(30) NOT NULL, selection_status varchar(30) NOT NULL, expires_at timestamptz NULL,
              verified_at timestamptz NULL, archived_at timestamptz NULL, notes text NULL, version integer NOT NULL DEFAULT 1,
              created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE job_applications (
              id uuid PRIMARY KEY, job_posting_id uuid NOT NULL REFERENCES job_postings(id) ON DELETE RESTRICT,
              status varchar(30) NOT NULL, channel varchar(30) NULL, application_email varchar(255) NULL, application_url text NULL,
              external_application_id varchar(500) NULL, applied_at timestamptz NULL, last_activity_at timestamptz NULL,
              notes text NULL, version integer NOT NULL DEFAULT 1, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE UNIQUE INDEX ix_job_applications_job_posting_id ON job_applications(job_posting_id);
            CREATE TABLE job_application_events (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id) ON DELETE RESTRICT,
              event_type varchar(50) NOT NULL, from_status varchar(30) NULL, to_status varchar(30) NULL, actor_type varchar(30) NOT NULL,
              actor_admin_user_id uuid NULL, note text NULL, metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
              occurred_at timestamptz NOT NULL, created_at timestamptz NOT NULL);
            CREATE TABLE job_application_documents (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id) ON DELETE RESTRICT,
              document_type varchar(50) NOT NULL, version_label varchar(100) NOT NULL, file_name varchar(500) NULL,
              storage_key varchar(1000) NULL, content_hash varchar(64) NULL, metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
              created_at timestamptz NOT NULL, removed_at timestamptz NULL);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilBlockedAsync(string connectionString, int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(timeout.Token);
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@process_id)) > 0;", connection);
            command.Parameters.AddWithValue("process_id", processId);
            if (await command.ExecuteScalarAsync(timeout.Token) is true) return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static string Schema() => "job_application_lock_" + Guid.NewGuid().ToString("N");
    private static string Connection(string? schema=null)
    {
        var value=Environment.GetEnvironmentVariable(ConnectionVariable)??throw new InvalidOperationException($"{ConnectionVariable} was not configured.");
        var builder=new NpgsqlConnectionStringBuilder(value);
        if(builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true||builder.Host is not("localhost" or "127.0.0.1"))throw new InvalidOperationException("Job application tests require a local *_tests PostgreSQL database.");
        if(schema is not null)builder.SearchPath=schema;
        return builder.ConnectionString;
    }

    private sealed class PausingTransactionFactory(IJobApplicationCreationTransactionFactory inner):IJobApplicationCreationTransactionFactory
    {
        private readonly TaskCompletionSource locked=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LockAcquired=>locked.Task;
        public async Task<IJobApplicationCreationTransaction> BeginAsync(Guid jobPostingId,CancellationToken cancellationToken=default)
        {
            var transaction=await inner.BeginAsync(jobPostingId,cancellationToken);locked.TrySetResult();
            try{await released.Task.WaitAsync(cancellationToken);return transaction;}
            catch{await transaction.DisposeAsync();throw;}
        }
        public void Release()=>released.TrySetResult();
    }

    private sealed class UpdateStartedInterceptor:DbCommandInterceptor
    {
        private readonly TaskCompletionSource<int> processId=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<int> ProcessId=>processId.Task;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result){Observe(command);return result;}
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default){Observe(command);return ValueTask.FromResult(result);}
        private void Observe(DbCommand command){if(command.CommandText.Contains("UPDATE job_postings",StringComparison.OrdinalIgnoreCase)&&command.Connection is NpgsqlConnection connection)processId.TrySetResult(connection.ProcessID);}
    }

    private sealed class CurrentUser:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>null;public string? Email=>"admin@example.com";}
    private sealed class FixedTimeProvider:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
}
