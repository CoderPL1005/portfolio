using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Abstractions.Submission;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class SubmissionAttemptConcurrencyPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ApplicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClientRequestId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AttemptId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ManifestHash = new('b', 64);

    [PostgresFact]
    public async Task Concurrent_duplicate_creation_returns_one_durable_attempt_and_event()
    {
        var schema = "submission_attempt_" + Guid.NewGuid().ToString("N");
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            await SeedAsync(connectionString);
            var results = await Task.WhenAll(AttemptAsync(connectionString), AttemptAsync(connectionString));

            Assert.Equal(results[0].Id, results[1].Id);
            await using var verify = Context(connectionString);
            Assert.Single(await verify.SubmissionAttempts.AsNoTracking().ToListAsync());
            Assert.Single(await verify.SubmissionAttemptEvents.AsNoTracking().ToListAsync());
            var application = await verify.JobApplications.AsNoTracking().SingleAsync();
            Assert.Equal("DRAFT", application.Status);
            Assert.Equal(7, application.Version);
            Assert.Null(application.AppliedAt);
        }
        finally { await DropAsync(Connection(), schema); }
    }

    [PostgresFact]
    public async Task Concurrent_different_requests_create_only_one_non_retryable_attempt_for_the_same_context()
    {
        var schema = "submission_attempt_" + Guid.NewGuid().ToString("N");
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            await SeedAsync(connectionString);
            var outcomes = await Task.WhenAll(
                AttemptOutcomeAsync(connectionString, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
                AttemptOutcomeAsync(connectionString, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));

            Assert.Single(outcomes, outcome => outcome.Result is not null);
            Assert.Single(outcomes, outcome => outcome.ErrorCode == "SUBMISSION_ATTEMPT_ACTIVE_EXISTS");
            await using var verify = Context(connectionString);
            Assert.Single(await verify.SubmissionAttempts.AsNoTracking().ToListAsync());
            Assert.Single(await verify.SubmissionAttemptEvents.AsNoTracking().ToListAsync());
        }
        finally { await DropAsync(Connection(), schema); }
    }

    [PostgresFact]
    public async Task Concurrent_execute_claim_invokes_adapter_exactly_once_without_holding_the_transaction()
    {
        var schema = "submission_attempt_" + Guid.NewGuid().ToString("N");
        var connectionString = Connection(schema); await CreateAsync(connectionString, schema);
        try
        {
            await SeedAsync(connectionString); await SeedApprovedAttemptAsync(connectionString);
            var adapter = new BlockingAdapter();
            var first = ExecuteAsync(connectionString, adapter);
            await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = await Assert.ThrowsAsync<ConflictException>(() => ExecuteAsync(connectionString, adapter));
            Assert.Equal("SUBMISSION_ATTEMPT_VERSION_CONFLICT", second.Code);
            adapter.Release.SetResult();
            var result = await first;

            Assert.Equal("FAILED", result.Status);
            Assert.Equal(1, adapter.CallCount);
            Assert.False(adapter.TransactionWasActive);
            await using var verify = Context(connectionString);
            Assert.Equal(["CREATED","APPROVED","SUBMITTING","FAILED"], await verify.SubmissionAttemptEvents
                .AsNoTracking().OrderBy(item=>item.OccurredAt).ThenBy(item=>item.Id).Select(item=>item.ToStatus).ToListAsync());
            Assert.Equal("DRAFT", (await verify.JobApplications.AsNoTracking().SingleAsync()).Status);
        }
        finally { await DropAsync(Connection(), schema); }
    }

    private static async Task<SubmissionAttemptResult> AttemptAsync(string connectionString, Guid? clientRequestId = null)
    {
        await using var db = Context(connectionString);
        var handler = new CreateSubmissionAttemptCommandHandler(db,
            new NpgsqlSubmissionAttemptCreationTransactionFactory(db),
            new NpgsqlSubmissionAttemptConflictDetector(), new(), new CurrentUser(), new FixedTimeProvider());
        return await handler.HandleAsync(new(ApplicationId, "EMAIL", clientRequestId ?? ClientRequestId, 7, 1, ManifestHash));
    }

    private static async Task<AttemptOutcome> AttemptOutcomeAsync(string connectionString, Guid clientRequestId)
    {
        try { return new(await AttemptAsync(connectionString, clientRequestId), null); }
        catch (ConflictException exception) { return new(null, exception.Code); }
    }

    private static async Task<SubmissionAttemptResult> ExecuteAsync(string connectionString, ISubmissionAdapter adapter)
    {
        await using var db=Context(connectionString);
        var factory=new NpgsqlSubmissionAttemptExecutionTransactionFactory(db);
        if(adapter is BlockingAdapter blocking)blocking.TransactionProbe=()=>db.Database.CurrentTransaction is not null;
        var handler=new ExecuteSubmissionAttemptCommandHandler(db,factory,[adapter],new(),new CurrentUser(),new FixedTimeProvider());
        return await handler.HandleAsync(new(AttemptId,2));
    }

    private static ApplicationDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, options => options.UseVector()).Options);

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            INSERT INTO admin_users(id) VALUES ('{{AdminId}}');
            INSERT INTO job_postings(id,company_name,position_title,location,description,technology_stack,verification_status,selection_status,version,created_at,updated_at)
            VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','Acme','Developer','Hanoi','Description','[]','VERIFIED','APPROVED',4,'{{Now:O}}','{{Now:O}}');
            INSERT INTO job_applications(id,job_posting_id,status,package_status,package_revision,package_job_posting_version,package_manifest_hash,package_finalized_at,package_finalized_by_admin_user_id,version,created_at,updated_at)
            VALUES ('{{ApplicationId}}','dddddddd-dddd-dddd-dddd-dddddddddddd','DRAFT','FINALIZED',1,4,'{{ManifestHash}}','{{Now:O}}','{{AdminId}}',7,'{{Now:O}}','{{Now:O}}');
            INSERT INTO job_application_documents(id,job_application_id,document_type,version_label,file_name,storage_key,content_hash,content_type,file_size_bytes,package_revision,source_canonical_cv_version,metadata,created_at)
            VALUES ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee','{{ApplicationId}}','CV','Package revision 1','cv.pdf','applications/private/cv.pdf','{{new string('a',64)}}','application/pdf',1024,1,3,'{}','{{Now:O}}');
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedApprovedAttemptAsync(string connectionString)
    {
        await using var connection=new NpgsqlConnection(connectionString);await connection.OpenAsync();
        await using var command=new NpgsqlCommand($$"""
            INSERT INTO submission_attempts(id,job_application_id,provider,status,idempotency_key,package_revision,package_manifest_hash,application_version_at_creation,created_at,created_by_admin_user_id,version)
            VALUES ('{{AttemptId}}','{{ApplicationId}}','EMAIL','APPROVED','{{new string('c',64)}}',1,'{{ManifestHash}}',7,'{{Now:O}}','{{AdminId}}',2);
            INSERT INTO submission_attempt_events(id,submission_attempt_id,from_status,to_status,actor_admin_user_id,occurred_at,created_at) VALUES
            ('11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa','{{AttemptId}}',NULL,'CREATED','{{AdminId}}','{{Now:O}}','{{Now:O}}'),
            ('22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa','{{AttemptId}}','CREATED','APPROVED','{{AdminId}}','{{Now.AddTicks(10):O}}','{{Now.AddTicks(10):O}}');
            """,connection);await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SCHEMA "{{schema}}";
            CREATE TABLE admin_users (id uuid PRIMARY KEY);
            CREATE TABLE job_postings (
              id uuid PRIMARY KEY, company_name varchar(255) NOT NULL, position_title varchar(255) NOT NULL, location varchar(255) NOT NULL,
              employment_type varchar(50), workplace_type varchar(50), salary_minimum numeric(18,2), salary_maximum numeric(18,2), salary_currency varchar(3), salary_period varchar(30),
              experience_requirements text, description text NOT NULL, technology_stack jsonb NOT NULL, application_email varchar(255), application_url text,
              verification_status varchar(30) NOT NULL, selection_status varchar(30) NOT NULL, expires_at timestamptz, verified_at timestamptz, archived_at timestamptz, notes text,
              version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE job_applications (
              id uuid PRIMARY KEY, job_posting_id uuid NOT NULL REFERENCES job_postings(id), status varchar(30) NOT NULL, channel varchar(30), application_email varchar(255), application_url text,
              external_application_id varchar(500), applied_at timestamptz, last_activity_at timestamptz, notes text,
              package_status varchar(30) NOT NULL, package_revision integer NOT NULL, package_job_posting_version integer, package_manifest_hash varchar(64),
              package_finalized_at timestamptz, package_finalized_by_admin_user_id uuid REFERENCES admin_users(id), version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE job_application_documents (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id), document_type varchar(50) NOT NULL, version_label varchar(100) NOT NULL,
              file_name varchar(500), storage_key varchar(1000), content_hash varchar(64), content_type varchar(100), file_size_bytes bigint, package_revision integer,
              source_canonical_cv_version integer, metadata jsonb NOT NULL, created_at timestamptz NOT NULL, removed_at timestamptz);
            CREATE UNIQUE INDEX uq_job_application_documents_managed_cv_revision ON job_application_documents(job_application_id,package_revision)
              WHERE document_type='CV' AND package_revision IS NOT NULL AND removed_at IS NULL;
            CREATE TABLE submission_attempts (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id), provider varchar(30) NOT NULL, status varchar(30) NOT NULL,
              idempotency_key varchar(64) NOT NULL, package_revision integer NOT NULL, package_manifest_hash varchar(64) NOT NULL,
              application_version_at_creation integer NOT NULL, created_at timestamptz NOT NULL, created_by_admin_user_id uuid NOT NULL REFERENCES admin_users(id),
              started_at timestamptz, completed_at timestamptz, provider_submission_id varchar(500), failure_code varchar(100), failure_message varchar(2000), version integer NOT NULL);
            CREATE UNIQUE INDEX uq_submission_attempts_idempotency_key ON submission_attempts(idempotency_key);
            CREATE UNIQUE INDEX uq_submission_attempts_non_retryable_context
              ON submission_attempts(job_application_id,package_revision,provider)
              WHERE status IN ('CREATED','APPROVED','SUBMITTING','SUCCEEDED','UNKNOWN');
            CREATE TABLE submission_attempt_events (
              id uuid PRIMARY KEY, submission_attempt_id uuid NOT NULL REFERENCES submission_attempts(id), from_status varchar(30), to_status varchar(30) NOT NULL,
              actor_admin_user_id uuid NOT NULL REFERENCES admin_users(id), occurred_at timestamptz NOT NULL, created_at timestamptz NOT NULL);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(string connectionString,string schema){await using var connection=new NpgsqlConnection(connectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;",connection);await command.ExecuteNonQueryAsync();}
    private static string Connection(string? schema=null){var value=Environment.GetEnvironmentVariable(ConnectionVariable)??throw new InvalidOperationException($"{ConnectionVariable} was not configured.");var builder=new NpgsqlConnectionStringBuilder(value);if(builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true||builder.Host is not ("localhost" or "127.0.0.1"))throw new InvalidOperationException("Submission attempt tests require a local *_tests PostgreSQL database.");if(schema is not null)builder.SearchPath=schema;return builder.ConnectionString;}
    private sealed class CurrentUser:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>AdminId;public string? Email=>"admin@example.com";}
    private sealed class FixedTimeProvider:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed record AttemptOutcome(SubmissionAttemptResult? Result, string? ErrorCode);
    private sealed class BlockingAdapter:ISubmissionAdapter
    {
        private int calls;public string Provider=>"EMAIL";public SubmissionAdapterCapabilities Capabilities=>new(true,false,false,false,true);public int CallCount=>calls;public Func<bool>? TransactionProbe{get;set;}public bool TransactionWasActive{get;private set;}public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(SubmissionRequest request)=>true;
        public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request,CancellationToken cancellationToken=default){Interlocked.Increment(ref calls);TransactionWasActive=TransactionProbe?.Invoke()==true;Entered.TrySetResult();await Release.Task.WaitAsync(cancellationToken);return new("FAILURE",null,"REJECTED","Rejected safely.");}
    }
}
