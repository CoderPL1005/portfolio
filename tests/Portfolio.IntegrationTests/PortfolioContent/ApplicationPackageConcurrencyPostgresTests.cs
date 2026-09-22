using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class ApplicationPackageConcurrencyPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly byte[] Pdf = "%PDF-1.7\ncanonical"u8.ToArray();

    [PostgresFact]
    public async Task Concurrent_finalization_has_one_winner_one_snapshot_and_one_event()
    {
        var schema = "application_package_" + Guid.NewGuid().ToString("N");
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            var applicationId = Guid.NewGuid();
            await SeedAsync(connectionString, applicationId);
            var storage = new BarrierStorage(2);
            storage.Objects["canonical-cv/source.pdf"] = Pdf;
            var attempts = await Task.WhenAll(
                AttemptAsync(connectionString, storage, applicationId),
                AttemptAsync(connectionString, storage, applicationId));

            Assert.Single(attempts, attempt => attempt.Result is not null);
            Assert.Equal("APPLICATION_PACKAGE_ALREADY_FINALIZED", Assert.Single(attempts, attempt => attempt.Error is not null).Error!.Code);
            await using var verify = Context(connectionString);
            var application = await verify.JobApplications.AsNoTracking().SingleAsync();
            Assert.Equal("FINALIZED", application.PackageStatus);
            Assert.Equal(1, application.PackageRevision);
            Assert.Single(await verify.JobApplicationDocuments.AsNoTracking().ToListAsync());
            Assert.Single(await verify.JobApplicationEvents.AsNoTracking().Where(item => item.EventType == "PACKAGE_FINALIZED").ToListAsync());
            Assert.Equal(2, storage.Objects.Count);
        }
        finally { await DropAsync(Connection(), schema); }
    }

    private static async Task<(ApplicationPackageResult? Result, ConflictException? Error)> AttemptAsync(string connectionString, BarrierStorage storage, Guid applicationId)
    {
        await using var db = Context(connectionString);
        var handler = new FinalizeApplicationPackageCommandHandler(db, storage,
            new NpgsqlApplicationPackageFinalizationTransactionFactory(db), new NpgsqlApplicationPackageConflictDetector(),
            new ApplicationPackageReadinessEvaluator(), new CurrentUser(), new FixedTimeProvider(),
            NullLogger<FinalizeApplicationPackageCommandHandler>.Instance);
        try { return (await handler.HandleAsync(new(applicationId, 1, 5, 3)), null); }
        catch (ConflictException exception) { return (null, exception); }
    }

    private static ApplicationDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString, options => options.UseVector()).Options);

    private static async Task SeedAsync(string connectionString, Guid applicationId)
    {
        await using var db = Context(connectionString);
        var job = new JobPosting { Id=Guid.NewGuid(),CompanyName="Acme",PositionTitle="Developer",Location="Hanoi",Description="Description",TechnologyStack=JsonDocument.Parse("[]"),VerificationStatus="VERIFIED",SelectionStatus="APPROVED",Version=5,CreatedAt=Now,UpdatedAt=Now };
        db.JobPostings.Add(job);
        db.JobApplications.Add(new JobApplication { Id=applicationId,JobPostingId=job.Id,Status="DRAFT",Version=1,CreatedAt=Now,UpdatedAt=Now });
        db.CanonicalCvs.Add(new CanonicalCv { Id=Guid.NewGuid(),SingletonKey=1,StorageKey="canonical-cv/source.pdf",FileName="CV.pdf",ContentType="application/pdf",FileSizeBytes=Pdf.Length,ContentHash=Hash(Pdf),Version=3,CreatedAt=Now,UpdatedAt=Now });
        await db.SaveChangesAsync();
    }

    private static async Task CreateAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SCHEMA "{{schema}}";
            CREATE TABLE admin_users (id uuid PRIMARY KEY);
            INSERT INTO admin_users(id) VALUES ('{{AdminId}}');
            CREATE TABLE job_postings (
              id uuid PRIMARY KEY, company_name varchar(255) NOT NULL, position_title varchar(255) NOT NULL, location varchar(255) NOT NULL,
              employment_type varchar(50), workplace_type varchar(50), salary_minimum numeric(18,2), salary_maximum numeric(18,2), salary_currency varchar(3), salary_period varchar(30),
              experience_requirements text, description text NOT NULL, technology_stack jsonb NOT NULL, application_email varchar(255), application_url text,
              verification_status varchar(30) NOT NULL, selection_status varchar(30) NOT NULL, expires_at timestamptz, verified_at timestamptz, archived_at timestamptz, notes text,
              version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE canonical_cvs (
              id uuid PRIMARY KEY, singleton_key smallint NOT NULL, storage_key varchar(1000) NOT NULL, file_name varchar(255) NOT NULL,
              content_type varchar(100) NOT NULL, file_size_bytes bigint NOT NULL, content_hash varchar(64) NOT NULL, version integer NOT NULL,
              created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE UNIQUE INDEX uq_canonical_cvs_singleton ON canonical_cvs(singleton_key);
            CREATE TABLE job_applications (
              id uuid PRIMARY KEY, job_posting_id uuid NOT NULL REFERENCES job_postings(id), status varchar(30) NOT NULL, channel varchar(30), application_email varchar(255), application_url text,
              external_application_id varchar(500), applied_at timestamptz, last_activity_at timestamptz, notes text,
              package_status varchar(30) NOT NULL DEFAULT 'DRAFT', package_revision integer NOT NULL DEFAULT 0, package_job_posting_version integer,
              package_manifest_hash varchar(64), package_finalized_at timestamptz, package_finalized_by_admin_user_id uuid REFERENCES admin_users(id),
              version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE job_application_documents (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id), document_type varchar(50) NOT NULL, version_label varchar(100) NOT NULL,
              file_name varchar(500), storage_key varchar(1000), content_hash varchar(64), content_type varchar(100), file_size_bytes bigint, package_revision integer,
              source_canonical_cv_version integer, metadata jsonb NOT NULL, created_at timestamptz NOT NULL, removed_at timestamptz);
            CREATE UNIQUE INDEX uq_job_application_documents_managed_cv_revision ON job_application_documents(job_application_id,package_revision)
              WHERE document_type='CV' AND package_revision IS NOT NULL AND removed_at IS NULL;
            CREATE TABLE job_application_events (
              id uuid PRIMARY KEY, job_application_id uuid NOT NULL REFERENCES job_applications(id), event_type varchar(50) NOT NULL, from_status varchar(30), to_status varchar(30),
              actor_type varchar(30) NOT NULL, actor_admin_user_id uuid REFERENCES admin_users(id), note text, metadata jsonb NOT NULL, occurred_at timestamptz NOT NULL, created_at timestamptz NOT NULL);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(string connectionString,string schema){await using var connection=new NpgsqlConnection(connectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;",connection);await command.ExecuteNonQueryAsync();}
    private static string Connection(string? schema=null){var value=Environment.GetEnvironmentVariable(ConnectionVariable)??throw new InvalidOperationException($"{ConnectionVariable} was not configured.");var builder=new NpgsqlConnectionStringBuilder(value);if(builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true||builder.Host is not ("localhost" or "127.0.0.1"))throw new InvalidOperationException("Application package tests require a local *_tests PostgreSQL database.");if(schema is not null)builder.SearchPath=schema;return builder.ConnectionString;}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class CurrentUser:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>AdminId;public string? Email=>"admin@example.com";}
    private sealed class FixedTimeProvider:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class BarrierStorage(int participants):IPrivateFileStorage
    {
        private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);private int arrivals;public ConcurrentDictionary<string,byte[]> Objects{get;}=[];
        public async Task UploadAsync(string key,Stream content,string contentType,CancellationToken cancellationToken=default){using var target=new MemoryStream();await content.CopyToAsync(target,cancellationToken);Objects[key]=target.ToArray();if(Interlocked.Increment(ref arrivals)==participants)release.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(10),cancellationToken);}
        public Task<Stream> OpenReadAsync(string key,long maximumBytes,CancellationToken cancellationToken=default)=>Task.FromResult<Stream>(new MemoryStream(Objects[key],false));
        public Task DeleteAsync(string key,CancellationToken cancellationToken=default){Objects.TryRemove(key,out _);return Task.CompletedTask;}
    }
}
