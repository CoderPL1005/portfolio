using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class CanonicalCvConcurrencyPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly byte[] Pdf = "%PDF-1.7\nconcurrent"u8.ToArray();

    [PostgresFact]
    public async Task Concurrent_first_uploads_create_one_row_and_clean_the_losing_object()
    {
        var schema = Schema();
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            var storage = new BarrierStorage(2);
            var attempts = await Task.WhenAll(
                AttemptAsync(connectionString, storage, expectedVersion: 0),
                AttemptAsync(connectionString, storage, expectedVersion: 0));

            Assert.Single(attempts, attempt => attempt.Result is not null);
            Assert.Equal("CANONICAL_CV_VERSION_CONFLICT",
                Assert.Single(attempts, attempt => attempt.Error is not null).Error!.Code);
            await using var verify = Context(connectionString);
            var row = await verify.CanonicalCvs.AsNoTracking().SingleAsync();
            Assert.Equal(1, row.Version);
            Assert.Single(storage.Objects);
            Assert.True(storage.Objects.ContainsKey(row.StorageKey));
        }
        finally { await DropAsync(Connection(), schema); }
    }

    [PostgresFact]
    public async Task Concurrent_replacements_have_one_winner_and_clean_old_and_losing_objects()
    {
        var schema = Schema();
        var connectionString = Connection(schema);
        await CreateAsync(connectionString, schema);
        try
        {
            const string oldKey = "canonical-cv/old.pdf";
            await using (var seed = Context(connectionString))
            {
                seed.CanonicalCvs.Add(Entity(oldKey));
                await seed.SaveChangesAsync();
            }
            var storage = new BarrierStorage(2);
            storage.Objects.TryAdd(oldKey, Pdf);

            var attempts = await Task.WhenAll(
                AttemptAsync(connectionString, storage, expectedVersion: 1),
                AttemptAsync(connectionString, storage, expectedVersion: 1));

            Assert.Single(attempts, attempt => attempt.Result is not null);
            Assert.Equal("CANONICAL_CV_VERSION_CONFLICT",
                Assert.Single(attempts, attempt => attempt.Error is not null).Error!.Code);
            await using var verify = Context(connectionString);
            var row = await verify.CanonicalCvs.AsNoTracking().SingleAsync();
            Assert.Equal(2, row.Version);
            Assert.NotEqual(oldKey, row.StorageKey);
            Assert.Single(storage.Objects);
            Assert.True(storage.Objects.ContainsKey(row.StorageKey));
        }
        finally { await DropAsync(Connection(), schema); }
    }

    private static async Task<(CanonicalCvResult? Result, ConflictException? Error)> AttemptAsync(
        string connectionString, BarrierStorage storage, int expectedVersion)
    {
        await using var db = Context(connectionString);
        var handler = new UploadCanonicalCvCommandHandler(
            db, storage, new NpgsqlCanonicalCvConflictDetector(), new FixedTimeProvider(),
            NullLogger<UploadCanonicalCvCommandHandler>.Instance);
        try
        {
            return (await handler.HandleAsync(new UploadCanonicalCvCommand(
                new MemoryStream(Pdf, writable: false), "CV.pdf", "application/pdf", Pdf.LongLength,
                expectedVersion)), null);
        }
        catch (ConflictException exception) { return (null, exception); }
    }

    private static CanonicalCv Entity(string storageKey) => new()
    {
        Id = Guid.NewGuid(), SingletonKey = 1, StorageKey = storageKey, FileName = "CV.pdf",
        ContentType = "application/pdf", FileSizeBytes = Pdf.LongLength,
        ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pdf)).ToLowerInvariant(),
        Version = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static ApplicationDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, options => options.UseVector()).Options);

    private static async Task CreateAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SCHEMA "{{schema}}";
            CREATE TABLE canonical_cvs (
              id uuid PRIMARY KEY, singleton_key smallint NOT NULL, storage_key varchar(1000) NOT NULL,
              file_name varchar(255) NOT NULL, content_type varchar(100) NOT NULL,
              file_size_bytes bigint NOT NULL, content_hash varchar(64) NOT NULL,
              version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
              CONSTRAINT ck_canonical_cvs_singleton CHECK (singleton_key = 1));
            CREATE UNIQUE INDEX uq_canonical_cvs_singleton ON canonical_cvs(singleton_key);
            CREATE UNIQUE INDEX uq_canonical_cvs_storage_key ON canonical_cvs(storage_key);
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

    private static string Schema() => "canonical_cv_" + Guid.NewGuid().ToString("N");

    private static string Connection(string? schema = null)
    {
        var value = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} was not configured.");
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.Database?.EndsWith("_tests", StringComparison.OrdinalIgnoreCase) != true
            || builder.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Canonical CV tests require a local *_tests PostgreSQL database.");
        if (schema is not null) builder.SearchPath = schema;
        return builder.ConnectionString;
    }

    private sealed class BarrierStorage(int participants) : IPrivateFileStorage
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();

        public async Task UploadAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var target = new MemoryStream();
            await content.CopyToAsync(target, cancellationToken);
            Objects[storageKey] = target.ToArray();
            if (Interlocked.Increment(ref arrivals) == participants) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string storageKey, long maximumBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Objects[storageKey], writable: false));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Objects.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
    }
}
