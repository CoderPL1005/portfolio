using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class CanonicalCvFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_state_is_explicit_and_does_not_expose_a_storage_key()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var result = await new GetCanonicalCvQueryHandler(db).HandleAsync(new());
        Assert.False(result.IsConfigured);
        Assert.Equal(0, result.Version);
        Assert.DoesNotContain(typeof(CanonicalCvResult).GetProperties(), property => property.Name == "StorageKey");
    }

    [Fact]
    public async Task First_upload_validates_content_and_persists_server_owned_metadata()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var bytes = Pdf("first");
        var result = await Handler(db, storage).HandleAsync(Command(bytes, 0, "C:\\fakepath\\My<CV>.pdf"));

        Assert.True(result.IsConfigured);
        Assert.Equal(1, result.Version);
        Assert.Equal("My_CV_.pdf", result.FileName);
        Assert.Equal(bytes.Length, result.FileSizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.ContentHash);
        var entity = await db.CanonicalCvs.SingleAsync();
        Assert.StartsWith("canonical-cv/", entity.StorageKey);
        Assert.EndsWith(".pdf", entity.StorageKey);
        Assert.DoesNotContain("My", entity.StorageKey);
        Assert.Equal(bytes, storage.Objects[entity.StorageKey]);
    }

    [Fact]
    public async Task Replacement_uses_a_new_key_increments_version_and_deletes_the_old_object()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var handler = Handler(db, storage);
        var first = await handler.HandleAsync(Command(Pdf("first"), 0));
        var oldKey = (await db.CanonicalCvs.SingleAsync()).StorageKey;
        var second = await handler.HandleAsync(Command(Pdf("second"), first.Version, "new.pdf"));

        Assert.Equal(2, second.Version);
        Assert.Equal(first.Id, second.Id);
        Assert.Contains(oldKey, storage.DeletedKeys);
        Assert.DoesNotContain(oldKey, storage.Objects.Keys);
        Assert.Single(storage.Objects);
    }

    [Theory]
    [InlineData("text/plain", "%PDF-valid")]
    [InlineData("application/pdf", "not-a-pdf")]
    public async Task Non_pdf_or_invalid_magic_is_rejected_without_upload(string contentType, string value)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(db, storage).HandleAsync(new(new MemoryStream(bytes), "CV.pdf", contentType, bytes.Length, 0)));
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task Oversized_or_reported_size_mismatch_is_rejected_without_upload()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var oversized = new byte[10 * 1024 * 1024 + 1];
        "%PDF-"u8.CopyTo(oversized);
        await Assert.ThrowsAsync<ValidationException>(() => Handler(db, storage).HandleAsync(Command(oversized, 0)));
        var valid = Pdf("valid");
        await Assert.ThrowsAsync<ValidationException>(() => Handler(db, storage).HandleAsync(
            new(new MemoryStream(valid), "CV.pdf", "application/pdf", valid.Length + 1, 0)));
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task Expected_version_rules_fail_before_upload()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var handler = Handler(db, storage);
        var firstError = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(Pdf("first"), 2)));
        Assert.Equal("CANONICAL_CV_VERSION_CONFLICT", firstError.Code);
        var first = await handler.HandleAsync(Command(Pdf("first"), 0));
        var objectCount = storage.Objects.Count;
        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(Pdf("stale"), first.Version - 1)));
        Assert.Equal(objectCount, storage.Objects.Count);
    }

    [Fact]
    public async Task Create_and_replacement_database_failures_compensate_the_new_object()
    {
        await using var createDb = PublicPortfolioTests.CreateContext();
        var createStorage = new FakePrivateStorage();
        createDb.FailSaveChanges = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => Handler(createDb, createStorage).HandleAsync(Command(Pdf("first"), 0)));
        Assert.Empty(createStorage.Objects);
        Assert.Single(createStorage.DeletedKeys);

        await using var replaceDb = PublicPortfolioTests.CreateContext();
        var replaceStorage = new FakePrivateStorage();
        var first = await Handler(replaceDb, replaceStorage).HandleAsync(Command(Pdf("first"), 0));
        var originalKey = (await replaceDb.CanonicalCvs.AsNoTracking().SingleAsync()).StorageKey;
        replaceDb.FailSaveChanges = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => Handler(replaceDb, replaceStorage).HandleAsync(Command(Pdf("second"), first.Version)));
        Assert.True(replaceStorage.Objects.ContainsKey(originalKey));
        Assert.Single(replaceStorage.Objects);
    }

    [Fact]
    public async Task Old_object_cleanup_failure_does_not_invalidate_committed_replacement()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var first = await Handler(db, storage).HandleAsync(Command(Pdf("first"), 0));
        storage.FailDelete = true;
        var replacement = await Handler(db, storage).HandleAsync(Command(Pdf("second"), first.Version));
        Assert.Equal(2, replacement.Version);
        Assert.Equal(2, (await db.CanonicalCvs.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task Content_read_uses_private_storage_and_revalidates_size_and_hash()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakePrivateStorage();
        var uploaded = await Handler(db, storage).HandleAsync(Command(Pdf("content"), 0));
        var result = await new GetCanonicalCvContentQueryHandler(
            db, storage, NullLogger<GetCanonicalCvContentQueryHandler>.Instance).HandleAsync(new());
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(uploaded.FileName, result.FileName);
        Assert.Equal(Pdf("content"), result.Content);
        Assert.Single(storage.OpenedKeys);
    }

    private static UploadCanonicalCvCommandHandler Handler(ContentTestDbContext db, FakePrivateStorage storage) => new(
        db, storage, new FakeConflictDetector(), new FixedTimeProvider(),
        NullLogger<UploadCanonicalCvCommandHandler>.Instance);

    private static UploadCanonicalCvCommand Command(byte[] bytes, int version, string name = "CV.pdf") =>
        new(new MemoryStream(bytes), name, "application/pdf", bytes.Length, version);

    private static byte[] Pdf(string content) => System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\n" + content);

    private sealed class FakePrivateStorage : IPrivateFileStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public List<string> OpenedKeys { get; } = [];
        public bool FailDelete { get; set; }
        public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var target = new MemoryStream();
            await content.CopyToAsync(target, cancellationToken);
            Objects[key] = target.ToArray();
        }
        public Task<Stream> OpenReadAsync(string key, long maximumBytes, CancellationToken cancellationToken = default)
        {
            OpenedKeys.Add(key);
            return Task.FromResult<Stream>(new MemoryStream(Objects[key], writable: false));
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            if (FailDelete) throw new IOException("simulated private cleanup failure");
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConflictDetector : ICanonicalCvConflictDetector
    {
        public bool IsSingletonConflict(DbUpdateException exception) => false;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
