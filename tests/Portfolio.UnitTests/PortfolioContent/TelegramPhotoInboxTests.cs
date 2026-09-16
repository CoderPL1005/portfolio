using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class TelegramPhotoInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4];
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2];

    [Fact]
    public async Task Single_photo_selects_largest_variant_and_persists_one_received_attachment()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg");var storage=new FakeStorage();
        var result=await Handler(db,telegram,storage).HandleAsync(PhotoCommand(caption:"Role https://facebook.com/jobs/42",photos:
        [new("small","unique-small",320,240,100),new("large","unique-large",1280,960,200)]));

        Assert.Equal(TelegramIngestionStatuses.Created,result.Status);Assert.Equal("large",Assert.Single(telegram.DownloadedFileIds));
        var raw=Assert.Single(await db.RawJobPostings.ToListAsync());var attachment=Assert.Single(await db.RawJobPostingAttachments.ToListAsync());
        Assert.Equal("telegram:200:10",raw.IngestionKey);Assert.Equal("Role https://facebook.com/jobs/42",raw.RawContent);Assert.Equal(RawJobPostingSources.Facebook,raw.Source);Assert.Equal(RawJobPostingIngestionStatuses.Received,raw.IngestionStatus);Assert.Empty(db.JobPostings);
        Assert.Equal(raw.Id,attachment.RawJobPostingId);Assert.Equal(RawJobPostingAttachmentTypes.Image,attachment.AttachmentType);Assert.Equal("large",attachment.TelegramFileId);Assert.Equal("unique-large",attachment.TelegramFileUniqueId);Assert.Equal(10,attachment.SortOrder);Assert.Equal("image/jpeg",attachment.ContentType);Assert.Equal(Jpeg.Length,attachment.FileSizeBytes);Assert.StartsWith($"job-hunting/raw/{raw.Id:N}/",attachment.StorageKey);Assert.EndsWith(".jpg",attachment.StorageKey);Assert.Equal(attachment.StorageKey,Assert.Single(storage.UploadedKeys));
        Assert.Equal(attachment.ContentHash,raw.ContentHash);Assert.Equal(TelegramAcknowledgements.ScreenshotReceived,Assert.Single(telegram.Messages).Text);
    }

    [Fact]
    public async Task Image_only_retry_is_idempotent_but_same_file_in_a_new_message_is_allowed()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Png,"image/png");var storage=new FakeStorage();var handler=Handler(db,telegram,storage);
        await handler.HandleAsync(PhotoCommand());await handler.HandleAsync(PhotoCommand());await handler.HandleAsync(PhotoCommand(messageId:11));

        var raws=await db.RawJobPostings.OrderBy(x=>x.IngestionKey).ToListAsync();var attachments=await db.RawJobPostingAttachments.OrderBy(x=>x.TelegramMessageId).ToListAsync();
        Assert.Equal(2,raws.Count);Assert.Equal(2,attachments.Count);Assert.All(raws,x=>Assert.Equal("[telegram-image]",x.RawContent));Assert.Single(attachments.Select(x=>x.ContentHash).Distinct());Assert.Equal(2,telegram.DownloadedFileIds.Count);Assert.Equal(2,storage.UploadedKeys.Count);Assert.Equal(2,telegram.Messages.Count);
    }

    [Fact]
    public async Task Album_parts_share_one_raw_job_order_by_message_and_never_acknowledge_per_item()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg");var storage=new FakeStorage();var handler=Handler(db,telegram,storage);
        await handler.HandleAsync(PhotoCommand(messageId:20,mediaGroupId:"album-A",fileId:"second"));
        await handler.HandleAsync(PhotoCommand(messageId:10,mediaGroupId:"album-A",fileId:"first",caption:"Album caption"));
        await handler.HandleAsync(PhotoCommand(messageId:20,mediaGroupId:"album-A",fileId:"second"));

        var raw=Assert.Single(await db.RawJobPostings.ToListAsync());var attachments=await db.RawJobPostingAttachments.OrderBy(x=>x.SortOrder).ToListAsync();
        Assert.Equal("telegram-album:200:album-A",raw.IngestionKey);Assert.Equal("Album caption",raw.RawContent);Assert.Equal(new long[]{10,20},attachments.Select(x=>x.TelegramMessageId));Assert.Equal(2,attachments.Count);Assert.Equal(2,telegram.DownloadedFileIds.Count);Assert.Empty(telegram.Messages);
        var expected=Hash("telegram-image-album:v1\n"+string.Join('\n',attachments.Select(x=>x.ContentHash)));Assert.Equal(expected,raw.ContentHash);
    }

    [Fact]
    public async Task Different_albums_create_different_raw_jobs_even_for_the_same_file()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg");var handler=Handler(db,telegram,new FakeStorage());
        await handler.HandleAsync(PhotoCommand(mediaGroupId:"album-A"));await handler.HandleAsync(PhotoCommand(messageId:11,mediaGroupId:"album-B"));
        Assert.Equal(2,await db.RawJobPostings.CountAsync());Assert.Equal(2,await db.RawJobPostingAttachments.CountAsync());
    }

    [Fact]
    public async Task Album_limit_rejects_an_extra_part_before_another_download()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg");var handler=Handler(db,telegram,new FakeStorage(),maxAlbumImages:1);
        await handler.HandleAsync(PhotoCommand(mediaGroupId:"album-A"));var result=await handler.HandleAsync(PhotoCommand(messageId:11,mediaGroupId:"album-A"));
        Assert.Equal(TelegramIngestionStatuses.Unsupported,result.Status);Assert.Single(telegram.DownloadedFileIds);Assert.Single(db.RawJobPostingAttachments);
    }

    [Fact]
    public async Task Unauthorized_owner_is_rejected_before_download_or_storage()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg");var storage=new FakeStorage();
        var result=await Handler(db,telegram,storage).HandleAsync(PhotoCommand(senderId:999));
        Assert.Equal(TelegramIngestionStatuses.Unauthorized,result.Status);Assert.Empty(telegram.DownloadedFileIds);Assert.Empty(storage.UploadedKeys);Assert.Empty(db.RawJobPostings);
    }

    [Theory]
    [MemberData(nameof(InvalidImages))]
    public async Task Invalid_or_oversized_image_fails_closed(byte[] bytes,string? contentType,long maxBytes)
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(bytes,contentType);var storage=new FakeStorage();
        var result=await Handler(db,telegram,storage,maxBytes:maxBytes).HandleAsync(PhotoCommand());
        Assert.Equal(TelegramIngestionStatuses.Unsupported,result.Status);Assert.Empty(storage.UploadedKeys);Assert.Empty(db.RawJobPostings);Assert.Empty(db.RawJobPostingAttachments);
    }

    public static IEnumerable<object?[]> InvalidImages()=>
    [
        [Array.Empty<byte>(),"image/jpeg",1024L],
        ["not-image"u8.ToArray(),"text/html",1024L],
        [Jpeg,"image/png",1024L],
        [Jpeg,"image/jpeg",4L],
    ];

    [Fact]
    public async Task Download_and_storage_failures_persist_nothing()
    {
        await using var downloadDb=PublicPortfolioTests.CreateContext();var failingTelegram=new FakeTelegram(Jpeg,"image/jpeg"){FailDownload=true};
        await Assert.ThrowsAsync<HttpRequestException>(()=>Handler(downloadDb,failingTelegram,new FakeStorage()).HandleAsync(PhotoCommand()));Assert.Empty(downloadDb.RawJobPostings);

        await using var storageDb=PublicPortfolioTests.CreateContext();var storage=new FakeStorage{FailUpload=true};
        await Assert.ThrowsAsync<IOException>(()=>Handler(storageDb,new FakeTelegram(Jpeg,"image/jpeg"),storage).HandleAsync(PhotoCommand()));Assert.Empty(storageDb.RawJobPostings);Assert.NotEmpty(storage.DeletedKeys);
    }

    [Fact]
    public async Task Database_failure_after_upload_attempts_compensation()
    {
        await using var db=PublicPortfolioTests.CreateContext();db.FailSaveChanges=true;var storage=new FakeStorage();
        await Assert.ThrowsAsync<DbUpdateException>(()=>Handler(db,new FakeTelegram(Jpeg,"image/jpeg"),storage).HandleAsync(PhotoCommand()));
        Assert.Single(storage.UploadedKeys);Assert.Equal(storage.UploadedKeys,storage.DeletedKeys);Assert.Empty(db.JobPostings);
    }

    [Fact]
    public async Task Acknowledgement_failure_does_not_rollback_screenshot()
    {
        await using var db=PublicPortfolioTests.CreateContext();var telegram=new FakeTelegram(Jpeg,"image/jpeg"){FailSend=true};
        var result=await Handler(db,telegram,new FakeStorage()).HandleAsync(PhotoCommand());
        Assert.Equal(TelegramIngestionStatuses.Created,result.Status);Assert.Single(db.RawJobPostings);Assert.Single(db.RawJobPostingAttachments);
    }

    private static ProcessTelegramWebhookCommandHandler Handler(ContentTestDbContext db,FakeTelegram telegram,FakeStorage storage,long maxBytes=1024,int maxAlbumImages=10)=>new(db,telegram,storage,new NeverConflict(),Options.Create(new TelegramOptions{Enabled=true,BotToken="test",WebhookSecret=new('x',32),AllowedUserId=300,AllowedChatId=200,MaxImageBytes=maxBytes,MaxAlbumImages=maxAlbumImages}),new FixedTimeProvider(Now),NullLogger<ProcessTelegramWebhookCommandHandler>.Instance);
    private static ProcessTelegramWebhookCommand PhotoCommand(long messageId=10,string? mediaGroupId=null,string? caption=null,string fileId="photo",long? senderId=300,IReadOnlyCollection<TelegramPhotoSize>? photos=null)=>new(100,messageId,Now.ToUnixTimeSeconds(),null,caption,senderId,200,"private",mediaGroupId,photos??[new(fileId,"unique",800,600,Jpeg.Length)]);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed class NeverConflict:IIngestionKeyConflictDetector{public bool IsIngestionKeyConflict(DbUpdateException exception)=>false;public bool IsAttachmentDeliveryConflict(DbUpdateException exception)=>false;}
    private sealed class FakeTelegram(byte[] bytes,string? contentType):ITelegramBotClient
    {
        public bool FailDownload{get;init;}public bool FailSend{get;init;}public List<string> DownloadedFileIds{get;}=[];public List<(long ChatId,string Text)> Messages{get;}=[];
        public Task<TelegramDownloadedFile> DownloadFileAsync(string fileId,long maximumBytes,CancellationToken ct=default){DownloadedFileIds.Add(fileId);if(FailDownload)throw new HttpRequestException("sanitized simulated failure");return Task.FromResult(new TelegramDownloadedFile(bytes,contentType));}
        public Task SendMessageAsync(long chatId,string text,CancellationToken ct=default){if(FailSend)throw new HttpRequestException("simulated");Messages.Add((chatId,text));return Task.CompletedTask;}
    }
    private sealed class FakeStorage:IFileStorage
    {
        public bool FailUpload{get;init;}public List<string> UploadedKeys{get;}=[];public List<string> DeletedKeys{get;}=[];
        public Task<string> UploadAsync(string key,Stream content,string contentType,CancellationToken ct=default)=>throw new InvalidOperationException("Public upload must not be used for job screenshots.");
        public Task UploadPrivateAsync(string key,Stream content,string contentType,CancellationToken ct=default){UploadedKeys.Add(key);if(FailUpload)throw new IOException("simulated");return Task.CompletedTask;}
        public Task<Stream> OpenReadAsync(string key,long maximumBytes,CancellationToken ct=default)=>Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key,CancellationToken ct=default){DeletedKeys.Add(key);return Task.CompletedTask;}
    }
}
