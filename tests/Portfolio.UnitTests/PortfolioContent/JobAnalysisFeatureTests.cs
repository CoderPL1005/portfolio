using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobAnalysisFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 3, 0, 0, TimeSpan.Zero);
    private static readonly byte[] FirstImage = [1, 2, 3, 4];
    private static readonly byte[] SecondImage = [5, 6, 7];

    [Fact]
    public async Task One_image_creates_exactly_one_job_and_normalizes_raw()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, attachments, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        var analyzer = new FakeAnalyzer(Valid());

        var result = await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        Assert.Equal(result.Id, raw.JobPostingId);
        Assert.Equal(RawJobPostingIngestionStatuses.Normalized, raw.IngestionStatus);
        Assert.Equal(3, raw.Version);
        var job = Assert.Single(await db.JobPostings.ToListAsync());
        Assert.Equal(JobPostingVerificationStatuses.Pending, job.VerificationStatus);
        Assert.Equal(JobPostingSelectionStatuses.PendingAnalysis, job.SelectionStatus);
        Assert.Equal(["C#", "Angular"], job.TechnologyStack.RootElement.EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JobHashingForTest("Acme", "Developer"), raw.CompanyTitleFingerprint);
        Assert.Single(analyzer.Requests);
        Assert.Equal(attachments[0].ContentHash, analyzer.Requests[0].Images[0].ContentHash);
    }

    [Fact]
    public async Task Multiple_images_are_read_in_sort_order_and_sent_in_one_analysis()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, attachments, storage) = await SeedAsync(db,
            (SecondImage, "image/jpeg", 2), (FirstImage, "image/png", 1));
        var analyzer = new FakeAnalyzer(Valid());

        await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        var request = Assert.Single(analyzer.Requests);
        Assert.Equal(2, request.Images.Count);
        Assert.Equal([1, 2], request.Images.Select(x => x.Order));
        Assert.Equal([attachments.Single(x => x.SortOrder == 1).StorageKey, attachments.Single(x => x.SortOrder == 2).StorageKey], storage.OpenedKeys);
        Assert.Equal([FirstImage, SecondImage], request.Images.Select(x => x.Content), ByteArrayComparer.Instance);
        Assert.Single(await db.JobPostings.ToListAsync());
    }

    [Theory]
    [InlineData(JobAnalysisAssessments.NotAJobPosting)]
    [InlineData(JobAnalysisAssessments.MultipleJobPostings)]
    [InlineData(JobAnalysisAssessments.UnreadableOrInsufficient)]
    public async Task Non_single_assessments_fail_closed(string assessment)
    {
        await AssertRejectedAsync(Valid() with { Assessment = assessment });
    }

    [Fact] public Task Conflicting_fields_fail_closed() => AssertRejectedAsync(Valid() with { ConflictingFields = ["companyName"] });
    [Fact] public Task Missing_required_field_fails_closed() => AssertRejectedAsync(Valid() with { CompanyName = null });

    [Theory]
    [MemberData(nameof(InvalidResults))]
    public async Task Deterministic_validation_failures_leave_raw_retryable(JobAnalysisResult result)
    {
        await AssertRejectedAsync(result);
    }

    public static IEnumerable<object[]> InvalidResults()
    {
        yield return [Valid() with { ApplicationEmail = "invalid" }];
        yield return [Valid() with { ApplicationUrl = "ftp://example.com" }];
        yield return [Valid() with { SalaryMinimum = -1 }];
        yield return [Valid() with { SalaryMinimum = 10, SalaryMaximum = 5 }];
        yield return [Valid() with { SalaryMinimum = 10000000000000000m }];
        yield return [Valid() with { SalaryMinimum = 1.001m }];
        yield return [Valid() with { SalaryCurrency = "US" }];
        yield return [Valid() with { ApplicationEmail = new string('a', 250) + "@x.com" }];
        yield return [Valid() with { TechnologyStack = Enumerable.Range(0, 101).Select(x => $"T{x}").ToArray() }];
        yield return [Valid() with { TechnologyStack = [new string('x', 101)] }];
    }

    [Fact]
    public async Task Storage_failure_and_hash_mismatch_never_invoke_analyzer()
    {
        await using var failureDb = PublicPortfolioTests.CreateContext();
        var (failureRaw, _, failureStorage) = await SeedAsync(failureDb, (FirstImage, "image/png", 1));
        failureStorage.FailRead = true;
        var failureAnalyzer = new FakeAnalyzer(Valid());
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => Handler(failureDb, failureStorage, failureAnalyzer).HandleAsync(new(failureRaw.Id)));
        Assert.Empty(failureAnalyzer.Requests);
        Assert.Empty(failureDb.JobPostings);

        await using var hashDb = PublicPortfolioTests.CreateContext();
        var (hashRaw, hashAttachments, hashStorage) = await SeedAsync(hashDb, (FirstImage, "image/png", 1));
        hashAttachments[0].ContentHash = new string('0', 64);
        await hashDb.SaveChangesAsync();
        var hashAnalyzer = new FakeAnalyzer(Valid());
        await Assert.ThrowsAsync<ValidationException>(() => Handler(hashDb, hashStorage, hashAnalyzer).HandleAsync(new(hashRaw.Id)));
        Assert.Empty(hashAnalyzer.Requests);
        Assert.Empty(hashDb.JobPostings);
    }

    [Fact]
    public async Task Unsupported_attachment_is_rejected_before_storage_or_analyzer()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, attachments, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        attachments[0].ContentType = "image/webp";
        await db.SaveChangesAsync();
        var analyzer = new FakeAnalyzer(Valid());

        await Assert.ThrowsAsync<ValidationException>(() => Handler(db, storage, analyzer).HandleAsync(new(raw.Id)));

        Assert.Empty(storage.OpenedKeys);
        Assert.Empty(analyzer.Requests);
    }

    [Fact]
    public async Task Missing_raw_missing_attachments_and_terminal_statuses_are_rejected()
    {
        await using var missingDb = PublicPortfolioTests.CreateContext();
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(missingDb, new FakeStorage(), new FakeAnalyzer(Valid())).HandleAsync(new(Guid.NewGuid())));

        await using var emptyDb = PublicPortfolioTests.CreateContext();
        var emptyRaw = Raw(Guid.NewGuid(), Now);
        emptyDb.RawJobPostings.Add(emptyRaw);
        await emptyDb.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Handler(emptyDb, new FakeStorage(), new FakeAnalyzer(Valid())).HandleAsync(new(emptyRaw.Id)));

        foreach (var status in new[] { RawJobPostingIngestionStatuses.Duplicate, RawJobPostingIngestionStatuses.Rejected })
        {
            await using var terminalDb = PublicPortfolioTests.CreateContext();
            var terminal = Raw(Guid.NewGuid(), Now);
            terminal.IngestionStatus = status;
            terminalDb.RawJobPostings.Add(terminal);
            await terminalDb.SaveChangesAsync();
            await Assert.ThrowsAsync<ConflictException>(() => Handler(terminalDb, new FakeStorage(), new FakeAnalyzer(Valid())).HandleAsync(new(terminal.Id)));
        }
    }

    [Fact]
    public async Task Provider_and_inline_size_failures_leave_raw_received_without_job()
    {
        foreach (var error in new Exception[] { new InvalidOperationException("secret provider body"), new JobAnalysisRequestTooLargeException() })
        {
            await using var db = PublicPortfolioTests.CreateContext();
            var (raw, _, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
            var analyzer = new FakeAnalyzer(error);
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => Handler(db, storage, analyzer).HandleAsync(new(raw.Id)));
            Assert.DoesNotContain("secret provider body", exception.Message);
            Assert.Equal(RawJobPostingIngestionStatuses.Received, raw.IngestionStatus);
            Assert.Null(raw.JobPostingId);
            Assert.Empty(db.JobPostings);
            Assert.Single(analyzer.Requests);
        }
    }

    [Fact]
    public async Task Already_normalized_returns_existing_job_without_storage_or_analyzer()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, _, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        var job = Job();
        db.JobPostings.Add(job);
        raw.JobPostingId = job.Id;
        raw.IngestionStatus = RawJobPostingIngestionStatuses.Normalized;
        await db.SaveChangesAsync();
        var analyzer = new FakeAnalyzer(Valid());

        var result = await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        Assert.Equal(job.Id, result.Id);
        Assert.Empty(storage.OpenedKeys);
        Assert.Empty(analyzer.Requests);
        Assert.Single(db.JobPostings);
    }

    [Fact]
    public async Task Exact_fingerprint_cache_is_revalidated_and_skips_provider()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, attachments, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        raw.Metadata = Cached(Fingerprint("models/job-model", attachments), Valid());
        await db.SaveChangesAsync();
        var analyzer = new FakeAnalyzer(Valid());

        await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        Assert.Empty(storage.OpenedKeys);
        Assert.Empty(analyzer.Requests);
        Assert.Single(db.JobPostings);
    }

    [Fact]
    public async Task Model_change_invalidates_cache_and_invokes_provider()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, attachments, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        raw.Metadata = Cached(Fingerprint("models/old-model", attachments), Valid());
        await db.SaveChangesAsync();
        var analyzer = new FakeAnalyzer(Valid());

        await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        Assert.Single(analyzer.Requests);
        Assert.Single(db.JobPostings);
    }

    [Fact]
    public async Task Received_list_is_filtered_ordered_paged_and_does_not_project_storage_data()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (received, _, _) = await SeedAsync(db, (FirstImage, "image/png", 1));
        var normalized = Raw(Guid.NewGuid(), Now.AddMinutes(1));
        normalized.IngestionStatus = RawJobPostingIngestionStatuses.Normalized;
        db.RawJobPostings.Add(normalized);
        await db.SaveChangesAsync();

        var result = await new GetRawJobPostingsQueryHandler(db).HandleAsync(new(1, 20, "received"));

        var item = Assert.Single(result.Items);
        Assert.Equal(received.Id, item.Id);
        Assert.Equal(1, item.AttachmentCount);
        Assert.DoesNotContain(item.GetType().GetProperties(), x => x.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Concurrent_finalization_returns_the_same_winner()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using var seedDb = SharedContext(databaseName, root);
        var (raw, _, storage) = await SeedAsync(seedDb, (FirstImage, "image/png", 1));
        var analyzer = new CoordinatedAnalyzer(Valid());
        await using var firstDb = SharedContext(databaseName, root);
        await using var secondDb = SharedContext(databaseName, root);

        var results = await Task.WhenAll(
            Handler(firstDb, storage, analyzer).HandleAsync(new(raw.Id)),
            Handler(secondDb, storage, analyzer).HandleAsync(new(raw.Id)));

        Assert.Equal(results[0].Id, results[1].Id);
        await using var verifyDb = SharedContext(databaseName, root);
        var winner = await verifyDb.RawJobPostings.AsNoTracking().SingleAsync(x => x.Id == raw.Id);
        Assert.Equal(RawJobPostingIngestionStatuses.Normalized, winner.IngestionStatus);
        Assert.Equal(winner.JobPostingId, results[0].Id);
        // EF InMemory does not implement the relational transaction that rolls back the losing insert.
        // The production atomicity guarantee is covered by the concurrency token plus relational SaveChanges transaction.
    }

    [Fact]
    public async Task Numeric_18_2_upper_boundary_and_email_within_255_are_accepted()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, _, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 54);
        var analyzer = new FakeAnalyzer(Valid() with
        {
            SalaryMinimum = 9999999999999999.99m,
            SalaryMaximum = 9999999999999999.99m,
            ApplicationEmail = email,
        });

        var result = await Handler(db, storage, analyzer).HandleAsync(new(raw.Id));

        Assert.Equal(9999999999999999.99m, result.SalaryMinimum);
        Assert.Equal(email, result.ApplicationEmail);
        Assert.Equal(RawJobPostingIngestionStatuses.Normalized, raw.IngestionStatus);
    }

    [Fact]
    public async Task Telegram_album_mutation_wins_over_stale_analysis_cache_without_silent_overwrite()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var rawId = Guid.NewGuid();
        var firstKey = $"job-hunting/raw/{rawId:N}/first.png";
        var firstHash = Sha(FirstImage);
        await using (var seed = SharedContext(databaseName, root))
        {
            var raw = Raw(rawId, Now);
            raw.Source = RawJobPostingSources.Other;
            raw.IngestionKey = "telegram-album:200:album-A";
            raw.RawContent = "[telegram-image-album]";
            raw.ContentHash = Sha(System.Text.Encoding.UTF8.GetBytes("telegram-image-album:v1\n" + firstHash));
            raw.Metadata = JsonDocument.Parse("{\"preserved\":\"yes\"}");
            seed.RawJobPostings.Add(raw);
            seed.RawJobPostingAttachments.Add(new RawJobPostingAttachment
            {
                Id = Guid.NewGuid(), RawJobPostingId = rawId, AttachmentType = RawJobPostingAttachmentTypes.Image,
                StorageKey = firstKey, ContentType = "image/png", ContentHash = firstHash, FileSizeBytes = FirstImage.Length,
                SortOrder = 10, TelegramMessageId = 10, TelegramFileId = "first", TelegramFileUniqueId = "first-unique",
                Width = 10, Height = 10, CreatedAt = Now,
            });
            await seed.SaveChangesAsync();
        }

        var storage = new FakeStorage();
        storage.Objects[firstKey] = FirstImage;
        var analyzer = new BlockingAnalyzer(Valid());
        await using var analyzeDb = SharedContext(databaseName, root);
        var analyzeTask = Handler(analyzeDb, storage, analyzer).HandleAsync(new(rawId));
        await analyzer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var telegramDb = SharedContext(databaseName, root))
        {
            var telegram = new TelegramClient([0xff, 0xd8, 0xff, 1, 2, 3]);
            var handler = new ProcessTelegramWebhookCommandHandler(
                telegramDb, telegram, storage, new NeverConflict(),
                Options.Create(new TelegramOptions
                {
                    Enabled = true, BotToken = "test", WebhookSecret = new string('x', 32),
                    AllowedUserId = 300, AllowedChatId = 200, MaxImageBytes = 1024, MaxAlbumImages = 10,
                }), new FixedTimeProvider(Now), NullLogger<ProcessTelegramWebhookCommandHandler>.Instance);
            var result = await handler.HandleAsync(new ProcessTelegramWebhookCommand(
                200, 20, Now.ToUnixTimeSeconds(), null, "Updated caption", 300, 200, "private", "album-A",
                [new TelegramPhotoSize("second", "second-unique", 20, 20, 6)]));
            Assert.Equal(TelegramIngestionStatuses.Created, result.Status);
        }

        analyzer.Release.TrySetResult();
        await Assert.ThrowsAsync<ConflictException>(() => analyzeTask);

        await using var verify = SharedContext(databaseName, root);
        var current = await verify.RawJobPostings.AsNoTracking().SingleAsync(x => x.Id == rawId);
        Assert.Equal(3, current.Version);
        Assert.Equal("Updated caption", current.RawContent);
        Assert.Equal("yes", current.Metadata.RootElement.GetProperty("preserved").GetString());
        Assert.Equal(200, current.Metadata.RootElement.GetProperty("telegramUpdateId").GetInt64());
        Assert.False(current.Metadata.RootElement.TryGetProperty("jobExtraction", out _));
        Assert.Equal(2, await verify.RawJobPostingAttachments.CountAsync(x => x.RawJobPostingId == rawId));
        Assert.Empty(await verify.JobPostings.ToListAsync());
    }

    [Fact]
    public async Task Telegram_album_reloads_and_retries_when_analysis_cache_advances_version_first()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var rawId = Guid.NewGuid();
        var firstHash = Sha(FirstImage);
        await using (var seed = SharedContext(databaseName, root))
        {
            var raw = Raw(rawId, Now);
            raw.IngestionKey = "telegram-album:200:album-B";
            raw.RawContent = "[telegram-image-album]";
            raw.Metadata = JsonDocument.Parse("{\"ingestionChannel\":\"TELEGRAM\"}");
            seed.RawJobPostings.Add(raw);
            seed.RawJobPostingAttachments.Add(new RawJobPostingAttachment
            {
                Id = Guid.NewGuid(), RawJobPostingId = rawId, AttachmentType = RawJobPostingAttachmentTypes.Image,
                StorageKey = "private/first", ContentType = "image/png", ContentHash = firstHash,
                FileSizeBytes = FirstImage.Length, SortOrder = 10, TelegramMessageId = 10,
                TelegramFileId = "first", TelegramFileUniqueId = "first-unique", Width = 10, Height = 10, CreatedAt = Now,
            });
            await seed.SaveChangesAsync();
        }

        var storage = new FakeStorage
        {
            BeforeFirstPrivateUpload = async () =>
            {
                await using var cacheDb = SharedContext(databaseName, root);
                var cached = await cacheDb.RawJobPostings.SingleAsync(x => x.Id == rawId);
                cached.Metadata = JsonDocument.Parse("{\"ingestionChannel\":\"TELEGRAM\",\"jobExtraction\":{\"fingerprint\":\"cache-winner\"}}");
                cached.Version++;
                cached.UpdatedAt = Now.AddMinutes(1);
                await cacheDb.SaveChangesAsync();
            },
        };
        await using var telegramDb = SharedContext(databaseName, root);
        telegramDb.ConcurrencyFailuresRemaining = 1;
        var handler = new ProcessTelegramWebhookCommandHandler(
            telegramDb, new TelegramClient([0xff, 0xd8, 0xff, 1, 2, 3]), storage, new NeverConflict(),
            Options.Create(new TelegramOptions
            {
                Enabled = true, BotToken = "test", WebhookSecret = new string('x', 32),
                AllowedUserId = 300, AllowedChatId = 200, MaxImageBytes = 1024, MaxAlbumImages = 10,
            }), new FixedTimeProvider(Now), NullLogger<ProcessTelegramWebhookCommandHandler>.Instance);

        var result = await handler.HandleAsync(new ProcessTelegramWebhookCommand(
            201, 20, Now.ToUnixTimeSeconds(), null, null, 300, 200, "private", "album-B",
            [new TelegramPhotoSize("second", "second-unique", 20, 20, 6)]));

        Assert.Equal(TelegramIngestionStatuses.Created, result.Status);
        await using var verify = SharedContext(databaseName, root);
        var current = await verify.RawJobPostings.AsNoTracking().SingleAsync(x => x.Id == rawId);
        Assert.Equal(4, current.Version);
        Assert.Equal("cache-winner", current.Metadata.RootElement.GetProperty("jobExtraction").GetProperty("fingerprint").GetString());
        Assert.Equal(201, current.Metadata.RootElement.GetProperty("telegramUpdateId").GetInt64());
        Assert.Equal(2, await verify.RawJobPostingAttachments.CountAsync(x => x.RawJobPostingId == rawId));
    }

    private static async Task AssertRejectedAsync(JobAnalysisResult result)
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var (raw, _, storage) = await SeedAsync(db, (FirstImage, "image/png", 1));
        await Assert.ThrowsAsync<ValidationException>(() => Handler(db, storage, new FakeAnalyzer(result)).HandleAsync(new(raw.Id)));
        Assert.Equal(RawJobPostingIngestionStatuses.Received, raw.IngestionStatus);
        Assert.Null(raw.JobPostingId);
        Assert.Equal(1, raw.Version);
        Assert.Empty(db.JobPostings);
    }

    private static AnalyzeRawJobPostingCommandHandler Handler(ContentTestDbContext db, FakeStorage storage, FakeAnalyzer analyzer) =>
        new(db, storage, analyzer, new FixedTimeProvider(Now), NullLogger<AnalyzeRawJobPostingCommandHandler>.Instance);

    private static AnalyzeRawJobPostingCommandHandler Handler(ContentTestDbContext db, FakeStorage storage, IJobAnalysisService analyzer) =>
        new(db, storage, analyzer, new FixedTimeProvider(Now), NullLogger<AnalyzeRawJobPostingCommandHandler>.Instance);

    private static ContentTestDbContext SharedContext(string name, InMemoryDatabaseRoot root) => new(
        new DbContextOptionsBuilder<ContentTestDbContext>().UseInMemoryDatabase(name, root).Options);

    private static async Task<(RawJobPosting Raw, List<RawJobPostingAttachment> Attachments, FakeStorage Storage)> SeedAsync(
        ContentTestDbContext db, params (byte[] Bytes, string Type, long Order)[] files)
    {
        var raw = Raw(Guid.NewGuid(), Now);
        var storage = new FakeStorage();
        var attachments = files.Select((file, index) =>
        {
            var key = $"private/{raw.Id:N}/{index}";
            storage.Objects[key] = file.Bytes;
            return new RawJobPostingAttachment
            {
                Id = Guid.NewGuid(), RawJobPostingId = raw.Id, AttachmentType = RawJobPostingAttachmentTypes.Image,
                StorageKey = key, ContentType = file.Type, ContentHash = Sha(file.Bytes), FileSizeBytes = file.Bytes.LongLength,
                SortOrder = file.Order, Width = 10, Height = 20, CreatedAt = Now,
            };
        }).ToList();
        db.RawJobPostings.Add(raw);
        db.RawJobPostingAttachments.AddRange(attachments);
        await db.SaveChangesAsync();
        return (raw, attachments, storage);
    }

    private static RawJobPosting Raw(Guid id, DateTimeOffset created) => new()
    {
        Id = id, Source = RawJobPostingSources.Manual, RawContent = "PWA screenshot submission",
        ContentHash = Sha("raw"u8.ToArray()), IngestionStatus = RawJobPostingIngestionStatuses.Received,
        Metadata = JsonDocument.Parse("{}"), DiscoveredAt = created, CreatedAt = created, UpdatedAt = created, Version = 1,
    };

    private static JobPosting Job() => new()
    {
        Id = Guid.NewGuid(), CompanyName = "Existing", PositionTitle = "Role", Location = "Hanoi", Description = "Description",
        TechnologyStack = JsonDocument.Parse("[]"), VerificationStatus = JobPostingVerificationStatuses.Pending,
        SelectionStatus = JobPostingSelectionStatuses.PendingAnalysis, Version = 1, CreatedAt = Now, UpdatedAt = Now,
    };

    private static JobAnalysisResult Valid() => new(
        JobAnalysisAssessments.SingleJobPosting, " Acme ", " Developer ", " Hanoi ", " Full time ", " Hybrid ",
        1000, 2000, "usd", "month", "2 years", "Build software", ["C#", " c# ", "Angular"],
        "jobs@example.com", "https://example.com/jobs/1", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), []);

    private static string Fingerprint(string model, IReadOnlyList<RawJobPostingAttachment> attachments) => Sha(System.Text.Encoding.UTF8.GetBytes(
        "job-extraction\n" + JobAnalysisContract.SchemaVersion + "\n" + JobAnalysisContract.PromptVersion + "\n"
        + model.Trim().ToLowerInvariant() + "\n" + string.Join('\n', attachments.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => $"{x.SortOrder}:{x.Id:N}:{x.ContentHash.ToLowerInvariant()}"))));

    private static JsonDocument Cached(string fingerprint, JobAnalysisResult result) => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        jobExtraction = new { fingerprint, validatedResult = result },
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static string Sha(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    private static string JobHashingForTest(string company, string title) => Sha(System.Text.Encoding.UTF8.GetBytes($"{company.ToLowerInvariant()}\n{title.ToLowerInvariant()}"));

    private sealed class FakeAnalyzer : IJobAnalysisService
    {
        private readonly JobAnalysisResult? result;
        private readonly Exception? error;
        public FakeAnalyzer(JobAnalysisResult result) => this.result = result;
        public FakeAnalyzer(Exception error) => this.error = error;
        public string ModelIdentifier { get; init; } = "models/job-model";
        public List<JobAnalysisRequest> Requests { get; } = [];
        public Task<JobAnalysisResult> AnalyzeAsync(JobAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (error is not null) throw error;
            return Task.FromResult(result!);
        }
    }

    private sealed class CoordinatedAnalyzer(JobAnalysisResult result) : IJobAnalysisService
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public string ModelIdentifier => "models/job-model";
        public async Task<JobAnalysisResult> AnalyzeAsync(JobAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class BlockingAnalyzer(JobAnalysisResult result) : IJobAnalysisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ModelIdentifier => "models/job-model";
        public async Task<JobAnalysisResult> AnalyzeAsync(JobAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class TelegramClient(byte[] content) : ITelegramBotClient
    {
        public Task<TelegramDownloadedFile> DownloadFileAsync(string fileId, long maximumBytes, CancellationToken ct = default) =>
            Task.FromResult(new TelegramDownloadedFile(content, "image/jpeg"));
        public Task SendMessageAsync(long chatId, string text, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NeverConflict : IIngestionKeyConflictDetector
    {
        public bool IsIngestionKeyConflict(DbUpdateException exception) => false;
        public bool IsAttachmentDeliveryConflict(DbUpdateException exception) => false;
    }

    private sealed class FakeStorage : IPrivateFileStorage
    {
        private int privateUploads;
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<string> OpenedKeys { get; } = [];
        public bool FailRead { get; set; }
        public Func<Task>? BeforeFirstPrivateUpload { get; init; }
        public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref privateUploads) == 1 && BeforeFirstPrivateUpload is not null)
                await BeforeFirstPrivateUpload();
            using var target = new MemoryStream();
            await content.CopyToAsync(target, ct);
            Objects[key] = target.ToArray();
        }
        public Task DeleteAsync(string key, CancellationToken ct = default) { Objects.Remove(key); return Task.CompletedTask; }
        public Task<Stream> OpenReadAsync(string key, long maximumBytes, CancellationToken ct = default)
        {
            OpenedKeys.Add(key);
            if (FailRead) throw new IOException("storage secret");
            return Task.FromResult<Stream>(new MemoryStream(Objects[key], writable: false));
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
