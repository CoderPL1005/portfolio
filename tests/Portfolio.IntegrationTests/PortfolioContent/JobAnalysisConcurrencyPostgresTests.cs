using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class JobAnalysisConcurrencyPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 3, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task Concurrent_analysis_commits_one_job_and_returns_the_winner()
    {
        var schema = "job_analysis_" + Guid.NewGuid().ToString("N");
        var connectionString = TestConnection(schema);
        await CreateSchemaAsync(connectionString, schema);
        try
        {
            var rawId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var bytes = new byte[] { 1, 2, 3 };
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            await using (var seed = Context(connectionString))
            {
                seed.RawJobPostings.Add(new RawJobPosting
                {
                    Id = rawId, Source = RawJobPostingSources.Manual, RawContent = "screenshots", ContentHash = hash,
                    IngestionStatus = RawJobPostingIngestionStatuses.Received, Metadata = JsonDocument.Parse("{}"),
                    DiscoveredAt = Now, CreatedAt = Now, UpdatedAt = Now, Version = 1,
                });
                seed.RawJobPostingAttachments.Add(new RawJobPostingAttachment
                {
                    Id = attachmentId, RawJobPostingId = rawId, AttachmentType = RawJobPostingAttachmentTypes.Image,
                    StorageKey = "private/image", ContentType = "image/png", ContentHash = hash, FileSizeBytes = bytes.Length,
                    SortOrder = 1, Width = 10, Height = 10, CreatedAt = Now,
                });
                await seed.SaveChangesAsync();
            }

            var analyzer = new CoordinatedAnalyzer();
            var storage = new Storage(bytes);
            await using var first = Context(connectionString);
            await using var second = Context(connectionString);
            var results = await Task.WhenAll(Handler(first, storage, analyzer).HandleAsync(new(rawId)), Handler(second, storage, analyzer).HandleAsync(new(rawId)));

            Assert.Equal(results[0].Id, results[1].Id);
            await using var verify = Context(connectionString);
            var raw = await verify.RawJobPostings.AsNoTracking().SingleAsync(x => x.Id == rawId);
            Assert.Equal(results[0].Id, raw.JobPostingId);
            Assert.Equal(RawJobPostingIngestionStatuses.Normalized, raw.IngestionStatus);
            Assert.Single(await verify.JobPostings.AsNoTracking().ToListAsync());
        }
        finally
        {
            await DropSchemaAsync(TestConnection(), schema);
        }
    }

    private static AnalyzeRawJobPostingCommandHandler Handler(ApplicationDbContext db, IPrivateFileStorage storage, IJobAnalysisService analyzer) =>
        new(db, storage, analyzer, new FixedTimeProvider(), NullLogger<AnalyzeRawJobPostingCommandHandler>.Instance);

    private static ApplicationDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString, options => options.UseVector()).Options);

    private static async Task CreateSchemaAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SCHEMA "{{schema}}";
            CREATE TABLE raw_job_postings (
              id uuid PRIMARY KEY, job_posting_id uuid NULL, source varchar(30) NOT NULL, source_external_id varchar(500) NULL,
              ingestion_key varchar(128) NULL, source_url text NULL, source_url_hash varchar(64) NULL, raw_content text NOT NULL,
              content_hash varchar(64) NOT NULL, company_title_fingerprint varchar(64) NULL, ingestion_status varchar(30) NOT NULL,
              duplicate_of_raw_job_posting_id uuid NULL, metadata jsonb NOT NULL DEFAULT '{}'::jsonb, discovered_at timestamptz NOT NULL,
              version integer NOT NULL DEFAULT 1, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE raw_job_posting_attachments (
              id uuid PRIMARY KEY, raw_job_posting_id uuid NOT NULL, attachment_type varchar(20) NOT NULL, storage_key varchar(1024) NOT NULL,
              content_type varchar(100) NOT NULL, content_hash varchar(64) NOT NULL, file_size_bytes bigint NOT NULL, sort_order bigint NOT NULL,
              telegram_message_id bigint NULL, telegram_file_id varchar(512) NULL, telegram_file_unique_id varchar(512) NULL,
              width integer NOT NULL, height integer NOT NULL, created_at timestamptz NOT NULL);
            CREATE TABLE job_postings (
              id uuid PRIMARY KEY, company_name varchar(255) NOT NULL, position_title varchar(255) NOT NULL, location varchar(255) NOT NULL,
              employment_type varchar(50) NULL, workplace_type varchar(50) NULL, salary_minimum numeric(18,2) NULL, salary_maximum numeric(18,2) NULL,
              salary_currency varchar(3) NULL, salary_period varchar(30) NULL, experience_requirements text NULL, description text NOT NULL,
              technology_stack jsonb NOT NULL DEFAULT '[]'::jsonb, application_email varchar(255) NULL, application_url text NULL,
              verification_status varchar(30) NOT NULL, selection_status varchar(30) NOT NULL, expires_at timestamptz NULL,
              verified_at timestamptz NULL, archived_at timestamptz NULL, notes text NULL, version integer NOT NULL DEFAULT 1,
              created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            CREATE TABLE job_applications (
              id uuid PRIMARY KEY, job_posting_id uuid NOT NULL, status varchar(30) NOT NULL, channel varchar(30) NULL,
              application_email varchar(255) NULL, application_url text NULL, external_application_id varchar(500) NULL,
              applied_at timestamptz NULL, last_activity_at timestamptz NULL, notes text NULL,
              package_status varchar(30) NOT NULL DEFAULT 'DRAFT', package_revision integer NOT NULL DEFAULT 0,
              package_job_posting_version integer NULL, package_manifest_hash varchar(64) NULL, package_finalized_at timestamptz NULL,
              package_finalized_by_admin_user_id uuid NULL, version integer NOT NULL DEFAULT 1,
              created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropSchemaAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string TestConnection(string? schema = null)
    {
        var value = Environment.GetEnvironmentVariable(ConnectionVariable) ?? throw new InvalidOperationException($"{ConnectionVariable} was not configured.");
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.Database?.EndsWith("_tests", StringComparison.OrdinalIgnoreCase) != true || builder.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Job analysis concurrency tests require a local *_tests PostgreSQL database.");
        if (schema is not null) builder.SearchPath = schema;
        return builder.ConnectionString;
    }

    private sealed class CoordinatedAnalyzer : IJobAnalysisService
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public string ModelIdentifier => "job-model";
        public async Task<JobAnalysisResult> AnalyzeAsync(JobAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(cancellationToken);
            return new(JobAnalysisAssessments.SingleJobPosting, "Acme", "Developer", "Hanoi", null, null, null, null, null, null,
                null, "Description", [], null, null, null, []);
        }
    }

    private sealed class Storage(byte[] bytes) : IPrivateFileStorage
    {
        public Task<Stream> OpenReadAsync(string key, long maximumBytes, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(bytes, false));
        public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
