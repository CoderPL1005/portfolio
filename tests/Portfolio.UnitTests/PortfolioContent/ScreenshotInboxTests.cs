using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ScreenshotInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task One_submission_persists_one_received_raw_and_ordered_attachments_only()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakeStorage();
        var handler = Handler(db, storage);
        var first = Png(320, 640, 1);
        var second = Jpeg(720, 1280);

        var result = await handler.HandleAsync(Command(Guid.NewGuid(),
            Upload(first, "image/png"), Upload(second, "image/jpeg")));

        Assert.True(result.Created);
        Assert.Equal(2, result.AttachmentCount);
        var raw = Assert.Single(await db.RawJobPostings.ToListAsync());
        Assert.Equal(result.RawJobPostingId, raw.Id);
        Assert.Null(raw.JobPostingId);
        Assert.Equal(RawJobPostingSources.Manual, raw.Source);
        Assert.Equal(RawJobPostingIngestionStatuses.Received, raw.IngestionStatus);
        Assert.Equal("PWA", raw.Metadata.RootElement.GetProperty("ingestionChannel").GetString());
        Assert.Empty(await db.JobPostings.ToListAsync());
        var attachments = await db.RawJobPostingAttachments.OrderBy(item => item.SortOrder).ToListAsync();
        Assert.Equal([1L, 2L], attachments.Select(item => item.SortOrder));
        Assert.Equal(["image/png", "image/jpeg"], attachments.Select(item => item.ContentType));
        Assert.Equal([(320, 640), (720, 1280)], attachments.Select(item => (item.Width, item.Height)));
        Assert.All(attachments, item =>
        {
            Assert.Null(item.TelegramMessageId);
            Assert.Null(item.TelegramFileId);
            Assert.Null(item.TelegramFileUniqueId);
            Assert.Matches("^[0-9a-f]{64}$", item.ContentHash);
            Assert.StartsWith($"job-hunting/raw/{raw.Id:N}/", item.StorageKey);
        });
        Assert.Equal(2, storage.Objects.Count);
    }

    [Fact]
    public async Task Same_owner_and_submission_is_idempotent_without_more_uploads()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakeStorage();
        var handler = Handler(db, storage);
        var id = Guid.NewGuid();
        var first = await handler.HandleAsync(Command(id, Upload(Png(10, 20), "image/png")));
        var second = await handler.HandleAsync(Command(id, Upload(Png(10, 20), "image/png")));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.RawJobPostingId, second.RawJobPostingId);
        Assert.Single(await db.RawJobPostings.ToListAsync());
        Assert.Single(await db.RawJobPostingAttachments.ToListAsync());
        Assert.Single(storage.Objects);
    }

    [Fact]
    public async Task Upload_and_database_failures_compensate_private_objects()
    {
        await using var uploadDb = PublicPortfolioTests.CreateContext();
        var failingUpload = new FakeStorage { FailUploadNumber = 2 };
        await Assert.ThrowsAsync<IOException>(() => Handler(uploadDb, failingUpload).HandleAsync(
            Command(Guid.NewGuid(), Upload(Png(10, 10), "image/png"), Upload(Png(20, 20), "image/png"))));
        Assert.Empty(failingUpload.Objects);
        Assert.Empty(uploadDb.RawJobPostings);

        await using var saveDb = PublicPortfolioTests.CreateContext();
        saveDb.FailSaveChanges = true;
        var saveStorage = new FakeStorage();
        await Assert.ThrowsAsync<DbUpdateException>(() => Handler(saveDb, saveStorage).HandleAsync(
            Command(Guid.NewGuid(), Upload(Png(10, 10), "image/png"))));
        Assert.Empty(saveStorage.Objects);
        Assert.Single(saveStorage.DeletedKeys);
        Assert.Empty(saveDb.JobPostings);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task Invalid_or_spoofed_binary_is_rejected_before_upload(string declaredType)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakeStorage();
        await Assert.ThrowsAsync<ValidationException>(() => Handler(db, storage).HandleAsync(
            Command(Guid.NewGuid(), Upload(Png(10, 10), declaredType))));
        Assert.Empty(storage.Objects);
        Assert.Empty(db.RawJobPostings);
    }

    [Fact]
    public async Task Invalid_magic_or_dimensions_are_rejected_before_upload()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var storage = new FakeStorage();
        var handler = Handler(db, storage);
        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            Command(Guid.NewGuid(), Upload("not-an-image"u8.ToArray(), "image/png"))));
        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            Command(Guid.NewGuid(), Upload(Png(0, 10), "image/png"))));
        Assert.Empty(storage.Objects);
        Assert.Empty(db.RawJobPostings);
    }

    [Fact]
    public async Task Validator_enforces_count_individual_and_aggregate_limits()
    {
        var validator = new SubmitJobScreenshotsCommandValidator();
        var empty = await validator.ValidateAsync(new(Guid.Empty, []));
        Assert.Contains(empty, item => item.PropertyName == "submissionId");
        Assert.Contains(empty, item => item.PropertyName == "files");

        var tooMany = Enumerable.Range(0, 11).Select(_ => Sized(1, "image/png")).ToArray();
        Assert.Contains(await validator.ValidateAsync(new(Guid.NewGuid(), tooMany)), item => item.PropertyName == "files");
        Assert.Contains(await validator.ValidateAsync(new(Guid.NewGuid(), [Sized(0, "image/png")])), item => item.PropertyName == "files");
        Assert.Contains(await validator.ValidateAsync(new(Guid.NewGuid(), [Sized(10 * 1024 * 1024 + 1L, "image/png")])), item => item.PropertyName == "files");
        var tooLarge = Enumerable.Range(0, 6).Select(_ => Sized(9 * 1024 * 1024L, "image/png")).ToArray();
        Assert.Contains(await validator.ValidateAsync(new(Guid.NewGuid(), tooLarge)), item => item.Message.Contains("total"));
        Assert.Contains(await validator.ValidateAsync(new(Guid.NewGuid(), [Sized(1, "image/heic")])), item => item.Message.Contains("JPEG and PNG"));
    }

    private static SubmitJobScreenshotsCommandHandler Handler(ContentTestDbContext db, FakeStorage storage) =>
        new(db, storage, new CurrentUser(AdminId), new NeverConflict(), new FixedTimeProvider(Now), NullLogger<SubmitJobScreenshotsCommandHandler>.Instance);

    private static SubmitJobScreenshotsCommand Command(Guid id, params ScreenshotUpload[] files) => new(id, files);
    private static ScreenshotUpload Upload(byte[] bytes, string type) => new(new MemoryStream(bytes), type, bytes.LongLength);
    private static ScreenshotUpload Sized(long size, string type) => new(Stream.Null, type, size);

    private static byte[] Png(int width, int height, byte suffix = 0)
    {
        var bytes = new byte[25];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = suffix;
        return bytes;
    }

    private static byte[] Jpeg(int width, int height) =>
    [
        0xff, 0xd8,
        0xff, 0xc0, 0x00, 0x07, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0xff, 0xd9,
    ];

    private sealed class CurrentUser(Guid id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? AdminUserId => id;
        public string? Email => "admin@example.com";
    }
    private sealed class NeverConflict : IIngestionKeyConflictDetector
    {
        public bool IsIngestionKeyConflict(DbUpdateException exception) => false;
        public bool IsAttachmentDeliveryConflict(DbUpdateException exception) => false;
    }
    private sealed class FakeStorage : IPrivateFileStorage
    {
        private int uploads;
        public int? FailUploadNumber { get; init; }
        public HashSet<string> Objects { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            uploads++;
            if (uploads == FailUploadNumber) throw new IOException("Simulated private upload failure.");
            Objects.Add(key);
            return Task.CompletedTask;
        }
        public Task<Stream> OpenReadAsync(string key, long maximumBytes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            Objects.Remove(key);
            DeletedKeys.Add(key);
            return Task.CompletedTask;
        }
    }
}
