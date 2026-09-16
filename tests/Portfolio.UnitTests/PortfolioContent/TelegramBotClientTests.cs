using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Portfolio.Application.Common.Configuration;
using Portfolio.Infrastructure.Integrations.Telegram;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class TelegramBotClientTests
{
    [Fact]
    public async Task Send_message_posts_exact_chat_and_text_without_real_network_access()
    {
        HttpRequestMessage? captured=null;string? body=null;
        var handler=new StubHandler(async request=>{captured=request;body=await request.Content!.ReadAsStringAsync();return new(HttpStatusCode.OK);});
        var client=Client(handler);
        await client.SendMessageAsync(-123,"Xin chào ✅");
        Assert.Equal(HttpMethod.Post,captured!.Method);Assert.Equal("https://api.telegram.org/bot123:test-token/sendMessage",captured.RequestUri!.AbsoluteUri);
        using var json=JsonDocument.Parse(body!);Assert.Equal(-123,json.RootElement.GetProperty("chat_id").GetInt64());Assert.Equal("Xin chào ✅",json.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Non_success_uses_a_sanitized_exception()
    {
        var exception=await Assert.ThrowsAsync<TelegramDeliveryException>(()=>Client(new StubHandler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)))).SendMessageAsync(1,"text"));
        Assert.DoesNotContain("123:test-token",exception.Message);
    }

    [Fact]
    public async Task Download_uses_getFile_then_only_the_validated_Telegram_file_path()
    {
        var requests=new List<Uri>();var call=0;
        var handler=new StubHandler(request=>
        {
            requests.Add(request.RequestUri!);call++;
            return Task.FromResult(call==1
                ? new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{ok=true,result=new{file_path="photos/file_1.jpg"}})}
                : new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([0xff,0xd8,0xff,1])});
        });
        var file=await Client(handler).DownloadFileAsync("file id",1024);
        Assert.Equal(2,requests.Count);Assert.EndsWith("getFile?file_id=file%20id",requests[0].AbsoluteUri);Assert.EndsWith("/photos/file_1.jpg",requests[1].AbsoluteUri);Assert.Equal([0xff,0xd8,0xff,1],file.Content);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("https://attacker.example/file")]
    [InlineData("photos\\file.jpg")]
    public async Task Download_rejects_unsafe_file_paths_without_a_second_request(string filePath)
    {
        var calls=0;var handler=new StubHandler(_=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{ok=true,result=new{file_path=filePath}})});});
        var exception=await Assert.ThrowsAsync<TelegramDeliveryException>(()=>Client(handler).DownloadFileAsync("id",1024));
        Assert.Equal(1,calls);Assert.DoesNotContain("123:test-token",exception.Message);
    }

    [Fact]
    public async Task Download_enforces_the_byte_limit_and_sanitizes_failures()
    {
        var call=0;var handler=new StubHandler(_=>Task.FromResult(++call==1
            ? new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{ok=true,result=new{file_path="photos/file.jpg"}})}
            : new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[20])}));
        var exception=await Assert.ThrowsAsync<TelegramDeliveryException>(()=>Client(handler).DownloadFileAsync("id",10));
        Assert.DoesNotContain("123:test-token",exception.Message);
    }

    [Fact]
    public void Conflict_detector_accepts_only_the_ingestion_key_unique_constraint()
    {
        var detector=new NpgsqlIngestionKeyConflictDetector();
        Assert.True(detector.IsIngestionKeyConflict(DbException(PostgresErrorCodes.UniqueViolation,"uq_raw_job_postings_ingestion_key")));
        Assert.False(detector.IsIngestionKeyConflict(DbException(PostgresErrorCodes.UniqueViolation,"uq_raw_job_postings_source_url_hash")));
        Assert.False(detector.IsIngestionKeyConflict(DbException(PostgresErrorCodes.ForeignKeyViolation,"uq_raw_job_postings_ingestion_key")));
        Assert.False(detector.IsIngestionKeyConflict(new DbUpdateException("other")));
        Assert.True(detector.IsAttachmentDeliveryConflict(DbException(PostgresErrorCodes.UniqueViolation,"uq_raw_job_posting_attachments_delivery")));
        Assert.False(detector.IsAttachmentDeliveryConflict(DbException(PostgresErrorCodes.UniqueViolation,"uq_raw_job_postings_ingestion_key")));
    }

    private static TelegramBotClient Client(HttpMessageHandler handler)=>new(new HttpClient(handler),Options.Create(new TelegramOptions{BotToken="123:test-token"}));
    private static DbUpdateException DbException(string state,string constraint)=>new("write failed",new PostgresException("write failed","ERROR","ERROR",state,constraintName:constraint));
    private sealed class StubHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>response(request);}
}
