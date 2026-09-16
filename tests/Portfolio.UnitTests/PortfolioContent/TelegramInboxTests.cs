using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Constants;
using Portfolio.Infrastructure.Integrations.Telegram;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class TelegramInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Disabled_options_are_safe_and_enabled_options_require_complete_valid_configuration()
    {
        var validator = new TelegramOptionsValidator();
        Assert.True(validator.Validate(null, new TelegramOptions()).Succeeded);

        var missing = validator.Validate(null, new TelegramOptions { Enabled = true });
        Assert.False(missing.Succeeded);
        Assert.Equal(4, missing.Failures!.Count());

        var secret = "valid_webhook-secret_with_32_chars";
        var valid = validator.Validate(null, Options(secret).Value);
        Assert.True(valid.Succeeded);
        Assert.DoesNotContain(secret, string.Join(' ', missing.Failures!));
        Assert.False(validator.Validate(null, new TelegramOptions
        {
            Enabled = true, BotToken = "token", WebhookSecret = "trivial", AllowedUserId = 1, AllowedChatId = 1,
        }).Succeeded);
    }

    [Theory]
    [InlineData("https://facebook.com/jobs/1", "FACEBOOK")]
    [InlineData("https://www.facebook.com/jobs/1", "FACEBOOK")]
    [InlineData("https://m.facebook.com/jobs/1", "FACEBOOK")]
    [InlineData("https://instagram.com/p/1", "INSTAGRAM")]
    [InlineData("https://www.instagram.com/p/1", "INSTAGRAM")]
    [InlineData("https://topcv.vn/viec-lam/1", "TOPCV")]
    [InlineData("https://www.topcv.vn/viec-lam/1", "TOPCV")]
    [InlineData("https://vietnamworks.com/job/1", "VIETNAMWORKS")]
    [InlineData("https://www.vietnamworks.com/job/1", "VIETNAMWORKS")]
    [InlineData("https://evilfacebook.com/job", "MANUAL")]
    [InlineData("https://facebook.com.attacker.example/job", "MANUAL")]
    [InlineData("https://notinstagram.com/job", "MANUAL")]
    [InlineData("https://fake-topcv.vn/job", "MANUAL")]
    [InlineData("https://example.com/job", "MANUAL")]
    public void Source_classification_is_boundary_safe(string url, string expected) =>
        Assert.Equal(expected, TelegramInboxMapping.ClassifySource(url));

    [Fact]
    public void Content_and_url_parsing_are_deterministic_and_network_free()
    {
        Assert.Equal("text", TelegramInboxMapping.ExtractContent("text", "caption"));
        Assert.Equal("caption", TelegramInboxMapping.ExtractContent("  ", "caption"));
        Assert.Null(TelegramInboxMapping.ExtractContent(" \r\n", null));
        Assert.Equal("https://example.com/first", TelegramInboxMapping.SelectSourceUrl("See https://example.com/first."));
        Assert.Equal("http://example.com/job", TelegramInboxMapping.SelectSourceUrl("http://example.com/job"));
        Assert.Equal("https://topcv.vn/jobs/2", TelegramInboxMapping.SelectSourceUrl("Unknown https://example.com/1 then https://topcv.vn/jobs/2, okay"));
        Assert.Equal("https://instagram.com/p/first", TelegramInboxMapping.SelectSourceUrl("https://instagram.com/p/first https://facebook.com/second"));
        Assert.Null(TelegramInboxMapping.SelectSourceUrl("No URL or malformed http:// here"));
        Assert.Equal("telegram:-100123:42", TelegramInboxMapping.BuildIngestionKey(-100123, 42));
        Assert.Equal(Now, TelegramInboxMapping.ResolveMessageTime(long.MaxValue, Now));
    }

    [Fact]
    public async Task Authorized_message_maps_complete_raw_record_and_acknowledges_after_save()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var client = new FakeTelegramClient(() => Assert.Single(db.RawJobPostings));
        var handler = Handler(db, client);
        var unix = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var result = await handler.HandleAsync(Command(
            text: "  Role\r\nhttps://www.facebook.com/jobs/42  ", date: unix));

        Assert.Equal(TelegramIngestionStatuses.Created, result.Status);
        var raw = Assert.Single(db.RawJobPostings);
        Assert.Null(raw.JobPostingId); Assert.Equal(RawJobPostingSources.Facebook, raw.Source);
        Assert.Null(raw.SourceExternalId); Assert.Equal("telegram:200:10", raw.IngestionKey);
        Assert.Equal("https://www.facebook.com/jobs/42", raw.SourceUrl);
        Assert.Equal(64, raw.SourceUrlHash!.Length); Assert.Equal(64, raw.ContentHash.Length);
        Assert.Equal("  Role\r\nhttps://www.facebook.com/jobs/42  ", raw.RawContent);
        Assert.Null(raw.CompanyTitleFingerprint); Assert.Equal(RawJobPostingIngestionStatuses.Received, raw.IngestionStatus);
        Assert.Null(raw.DuplicateOfRawJobPostingId); Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unix), raw.DiscoveredAt);
        Assert.Equal(Now, raw.CreatedAt); Assert.Equal(Now, raw.UpdatedAt); Assert.Empty(db.JobPostings);
        Assert.Empty(db.RawJobPostingAttachments);
        var metadata = raw.Metadata.RootElement;
        Assert.Equal("TELEGRAM", metadata.GetProperty("ingestionChannel").GetString());
        Assert.Equal(100, metadata.GetProperty("telegramUpdateId").GetInt64());
        Assert.Equal(10, metadata.GetProperty("telegramMessageId").GetInt64());
        Assert.False(metadata.TryGetProperty("username", out _));
        Assert.Equal((200, TelegramAcknowledgements.Received("FACEBOOK")), Assert.Single(client.Messages));
    }

    [Fact]
    public async Task Exact_retry_is_idempotent_but_same_content_in_new_message_is_a_new_delivery()
    {
        await using var db = PublicPortfolioTests.CreateContext();var client = new FakeTelegramClient();var handler = Handler(db, client);
        var first = await handler.HandleAsync(Command(text: "same copied JD"));
        var retry = await handler.HandleAsync(Command(text: "same copied JD"));
        var second = await handler.HandleAsync(Command(messageId: 11, text: "same copied JD"));

        Assert.Equal(TelegramIngestionStatuses.Created, first.Status);
        Assert.Equal(TelegramIngestionStatuses.AlreadyReceived, retry.Status);
        Assert.Equal(TelegramIngestionStatuses.Created, second.Status);
        var rows = db.RawJobPostings.OrderBy(item => item.IngestionKey).ToArray();
        Assert.Equal(2, rows.Length); Assert.Equal(rows[0].ContentHash, rows[1].ContentHash);
        Assert.NotEqual(rows[0].IngestionKey, rows[1].IngestionKey); Assert.All(rows, row => Assert.Null(row.DuplicateOfRawJobPostingId));
        Assert.Equal(2, client.Messages.Count);
    }

    [Theory]
    [InlineData(null, 200, "private")]
    [InlineData(300, null, "private")]
    [InlineData(999, 200, "private")]
    [InlineData(300, 999, "private")]
    [InlineData(300, 200, "group")]
    public async Task Missing_or_wrong_owner_identity_is_rejected_without_storage_or_reply(int? user, int? chat, string type)
    {
        await using var db=PublicPortfolioTests.CreateContext();var client=new FakeTelegramClient();var result=await Handler(db,client).HandleAsync(Command(senderId:user,chatId:chat,chatType:type));
        Assert.Equal(TelegramIngestionStatuses.Unauthorized,result.Status);Assert.Empty(db.RawJobPostings);Assert.Empty(client.Messages);
    }

    [Fact]
    public async Task Unsupported_input_uses_caption_fallback_rules_and_guidance_only()
    {
        await using var db=PublicPortfolioTests.CreateContext();var client=new FakeTelegramClient();var handler=Handler(db,client);
        var unsupported=await handler.HandleAsync(Command(text:"  ",caption:null));Assert.Equal(TelegramIngestionStatuses.Unsupported,unsupported.Status);Assert.Empty(db.RawJobPostings);Assert.Equal(TelegramAcknowledgements.Unsupported,Assert.Single(client.Messages).Text);
        var caption=await handler.HandleAsync(Command(messageId:11,text:" ",caption:"caption JD"));Assert.Equal(TelegramIngestionStatuses.Created,caption.Status);Assert.Equal("caption JD",Assert.Single(db.RawJobPostings).RawContent);
    }

    [Fact]
    public async Task Acknowledgement_failure_never_rolls_back_a_committed_record()
    {
        await using var db=PublicPortfolioTests.CreateContext();var client=new FakeTelegramClient(throwOnSend:true);var result=await Handler(db,client).HandleAsync(Command());
        Assert.Equal(TelegramIngestionStatuses.Created,result.Status);Assert.Single(db.RawJobPostings);Assert.Empty(db.JobPostings);
    }

    private static ProcessTelegramWebhookCommandHandler Handler(ContentTestDbContext db,FakeTelegramClient client)=>new(db,client,new FakeStorage(),new NeverConflict(),Options(),new FixedTimeProvider(Now),NullLogger<ProcessTelegramWebhookCommandHandler>.Instance);
    private static IOptions<TelegramOptions> Options(string secret="valid_webhook-secret_with_32_chars")=>Microsoft.Extensions.Options.Options.Create(new TelegramOptions{Enabled=true,BotToken="test-token",WebhookSecret=secret,AllowedUserId=300,AllowedChatId=200});
    private static ProcessTelegramWebhookCommand Command(long messageId=10,string? text="Copied JD",string? caption=null,long? senderId=300,long? chatId=200,string chatType="private",long? date=null)=>new(100,messageId,date,text,caption,senderId,chatId,chatType);
    private sealed class NeverConflict:IIngestionKeyConflictDetector{public bool IsIngestionKeyConflict(Microsoft.EntityFrameworkCore.DbUpdateException exception)=>false;public bool IsAttachmentDeliveryConflict(Microsoft.EntityFrameworkCore.DbUpdateException exception)=>false;}
    private sealed class FakeTelegramClient(Action? beforeSend=null,bool throwOnSend=false):ITelegramBotClient
    {
        public List<(long ChatId,string Text)> Messages{get;}=[];
        public Task SendMessageAsync(long chatId,string text,CancellationToken cancellationToken=default){beforeSend?.Invoke();if(throwOnSend)throw new HttpRequestException("simulated");Messages.Add((chatId,text));return Task.CompletedTask;}
        public Task<TelegramDownloadedFile> DownloadFileAsync(string fileId,long maximumBytes,CancellationToken cancellationToken=default)=>Task.FromResult(new TelegramDownloadedFile([0xff,0xd8,0xff,0],"image/jpeg"));
    }
    private sealed class FakeStorage:IFileStorage{public Task<string> UploadAsync(string key,Stream content,string contentType,CancellationToken ct=default)=>Task.FromResult(key);public Task UploadPrivateAsync(string key,Stream content,string contentType,CancellationToken ct=default)=>Task.CompletedTask;public Task<Stream> OpenReadAsync(string key,long maximumBytes,CancellationToken ct=default)=>Task.FromResult<Stream>(new MemoryStream());public Task DeleteAsync(string key,CancellationToken ct=default)=>Task.CompletedTask;}
}
