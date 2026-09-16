using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Integrations;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class TelegramWebhookApiTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    private const string Secret = "integration_webhook-secret_32_chars";

    [Fact]
    public async Task Disabled_webhook_fails_closed_without_storage_or_outbound_call()
    {
        var setup=CreateFactory(enabled:false);using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,Update());
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Empty(await RawRows(factory));Assert.Empty(setup.Telegram.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task Missing_or_invalid_secret_is_rejected_before_processing(string? secret)
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/integrations/telegram/webhook"){Content=new StringContent("not-json")};
        if(secret is not null)request.Headers.Add("X-Telegram-Bot-Api-Secret-Token",secret);
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Empty(await RawRows(factory));Assert.Empty(setup.Telegram.Messages);
    }

    [Theory]
    [InlineData(999,200,"private")]
    [InlineData(300,999,"private")]
    [InlineData(300,200,"group")]
    public async Task Valid_secret_is_insufficient_without_exact_private_owner(long userId,long chatId,string chatType)
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,Update(userId:userId,chatId:chatId,chatType:chatType),Secret);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Empty(await RawRows(factory));Assert.Empty(setup.Telegram.Messages);
    }

    [Theory]
    [InlineData("https://facebook.com/jobs/1","FACEBOOK")]
    [InlineData("https://instagram.com/p/1","INSTAGRAM")]
    [InlineData("https://topcv.vn/viec-lam/1","TOPCV")]
    [InlineData("https://vietnamworks.com/job/1","VIETNAMWORKS")]
    [InlineData("Copied job description","MANUAL")]
    public async Task Authorized_message_needs_no_admin_jwt_and_persists_expected_source(string text,string source)
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,Update(text:text),Secret);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var raw=Assert.Single(await RawRows(factory));Assert.Equal(source,raw.Source);Assert.Equal("RECEIVED",raw.IngestionStatus);Assert.Equal("telegram:200:10",raw.IngestionKey);Assert.Null(raw.JobPostingId);Assert.Single(setup.Telegram.Messages);
    }

    [Fact]
    public async Task Caption_is_used_but_unsupported_media_creates_no_record()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var captionResponse=await PostAsync(client,Update(messageId:10,text:null,caption:"Caption job"),Secret);
        using var unsupportedResponse=await PostAsync(client,Update(messageId:11,text:null,caption:null),Secret);
        Assert.Equal(HttpStatusCode.OK,captionResponse.StatusCode);Assert.Equal(HttpStatusCode.OK,unsupportedResponse.StatusCode);Assert.Equal("Caption job",Assert.Single(await RawRows(factory)).RawContent);Assert.Equal(2,setup.Telegram.Messages.Count);Assert.Equal(TelegramAcknowledgements.Unsupported,setup.Telegram.Messages[1].Text);
    }

    [Fact]
    public async Task Unsupported_update_kind_is_ignored_without_storage_or_reply()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,new{update_id=101},Secret);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Empty(await RawRows(factory));Assert.Empty(setup.Telegram.Messages);
    }

    [Fact]
    public async Task Exact_retry_creates_one_row_and_one_acknowledgement()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();var update=Update();
        Assert.Equal(HttpStatusCode.OK,(await PostAsync(client,update,Secret)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await PostAsync(client,update,Secret)).StatusCode);
        Assert.Single(await RawRows(factory));Assert.Single(setup.Telegram.Messages);
    }

    [Fact]
    public async Task Same_content_with_different_message_identity_creates_two_rows_and_two_acknowledgements()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        await PostAsync(client,Update(messageId:10,text:"same JD"),Secret);await PostAsync(client,Update(messageId:11,text:"same JD"),Secret);
        var rows=await RawRows(factory);Assert.Equal(2,rows.Count);Assert.Single(rows.Select(x=>x.ContentHash).Distinct());Assert.Equal(2,rows.Select(x=>x.IngestionKey).Distinct().Count());Assert.Equal(2,setup.Telegram.Messages.Count);
    }

    [Fact]
    public async Task Acknowledgement_failure_keeps_committed_row_and_returns_success()
    {
        var setup=CreateFactory(failAcknowledgement:true);using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,Update(),Secret);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Single(await RawRows(factory));Assert.Equal(1,setup.Telegram.Attempts);
    }

    [Fact]
    public async Task Unsupported_acknowledgement_failure_returns_success_without_storage()
    {
        var setup=CreateFactory(failAcknowledgement:true);using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await PostAsync(client,Update(text:null),Secret);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Empty(await RawRows(factory));Assert.Equal(1,setup.Telegram.Attempts);
    }

    [Fact]
    public async Task Admin_job_hunting_endpoint_remains_jwt_protected()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();
        using var response=await client.GetAsync("/api/v1/admin/job-postings");Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Fact]
    public async Task Photo_payload_is_deserialized_but_invalid_secret_cannot_dispatch_or_download()
    {
        var setup=CreateFactory();using var factory=setup.Factory;using var client=factory.CreateClient();var update=PhotoUpdate();
        using var rejected=await PostAsync(client,update,"wrong-secret");Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);Assert.Equal(0,setup.Telegram.DownloadAttempts);
        using var accepted=await PostAsync(client,update,Secret);Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);Assert.Equal(1,setup.Telegram.DownloadAttempts);
        var handler=factory.Services.GetRequiredService<TelegramHandlerProbe>();Assert.Equal("album-1",handler.LastRequest!.MediaGroupId);Assert.Equal(2,handler.LastRequest.Photo!.Count);Assert.Equal("large",Assert.Single(setup.Telegram.DownloadedFileIds));
        var disabled=CreateFactory(enabled:false);using var disabledFactory=disabled.Factory;using var disabledClient=disabledFactory.CreateClient();using var unavailable=await PostAsync(disabledClient,update,Secret);Assert.Equal(HttpStatusCode.NotFound,unavailable.StatusCode);Assert.Equal(0,disabled.Telegram.DownloadAttempts);
    }

    private Setup CreateFactory(bool enabled=true,bool failAcknowledgement=false)
    {
        var telegram=new FakeTelegramClient(failAcknowledgement);var handler=new TelegramHandlerProbe(telegram);
        var factory=root.WithWebHostBuilder(builder=>
        {
            builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Telegram:Enabled"]=enabled.ToString(),["Telegram:BotToken"]="integration-test-token",["Telegram:WebhookSecret"]=Secret,["Telegram:AllowedUserId"]="300",["Telegram:AllowedChatId"]="200",
            }));
            builder.ConfigureServices(services=>
            {
                services.RemoveAll<ITelegramBotClient>();services.AddSingleton<ITelegramBotClient>(telegram);
                services.RemoveAll<IRequestHandler<ProcessTelegramWebhookCommand,TelegramWebhookResult>>();services.AddSingleton<IRequestHandler<ProcessTelegramWebhookCommand,TelegramWebhookResult>>(handler);services.AddSingleton(handler);
            });
        });
        return new(factory,telegram);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client,object update,string? secret=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/integrations/telegram/webhook"){Content=JsonContent.Create(update)};if(secret is not null)request.Headers.Add("X-Telegram-Bot-Api-Secret-Token",secret);return await client.SendAsync(request);
    }
    private static object Update(long messageId=10,string? text="Copied JD",string? caption=null,long userId=300,long chatId=200,string chatType="private")=>new{update_id=100,message=new{message_id=messageId,date=1_789_531_200L,text,caption,from=new{id=userId,username="ignored"},chat=new{id=chatId,type=chatType}}};
    private static object PhotoUpdate()=>new{update_id=101,message=new{message_id=12,date=1_789_531_200L,caption="Screenshot",media_group_id="album-1",photo=new[]{new{file_id="small",file_unique_id="u1",width=320,height=240,file_size=100L},new{file_id="large",file_unique_id="u2",width=1280,height=960,file_size=200L}},from=new{id=300},chat=new{id=200,type="private"}}};
    private static Task<List<RawJobPosting>> RawRows(WebApplicationFactory<Program> factory){var handler=factory.Services.GetRequiredService<TelegramHandlerProbe>();return Task.FromResult(handler.Rows.OrderBy(x=>x.IngestionKey).ToList());}
    private sealed record Setup(WebApplicationFactory<Program> Factory,FakeTelegramClient Telegram);
    private sealed class FakeTelegramClient(bool fail):ITelegramBotClient
    {
        public List<(long ChatId,string Text)> Messages{get;}=[];public int Attempts{get;private set;}public int DownloadAttempts{get;private set;}public List<string> DownloadedFileIds{get;}=[];
        public Task SendMessageAsync(long chatId,string text,CancellationToken cancellationToken=default){Attempts++;if(fail)throw new HttpRequestException("simulated");Messages.Add((chatId,text));return Task.CompletedTask;}
        public Task<TelegramDownloadedFile> DownloadFileAsync(string fileId,long maximumBytes,CancellationToken cancellationToken=default){DownloadAttempts++;DownloadedFileIds.Add(fileId);return Task.FromResult(new TelegramDownloadedFile([0xff,0xd8,0xff,0],"image/jpeg"));}
    }

    private sealed class TelegramHandlerProbe(FakeTelegramClient telegram):IRequestHandler<ProcessTelegramWebhookCommand,TelegramWebhookResult>
    {
        private readonly object gate=new();private readonly List<RawJobPosting> rows=[];
        public ProcessTelegramWebhookCommand? LastRequest{get;private set;}
        public IReadOnlyCollection<RawJobPosting> Rows{get{lock(gate)return rows.ToArray();}}
        public async Task<TelegramWebhookResult> HandleAsync(ProcessTelegramWebhookCommand request,CancellationToken cancellationToken=default)
        {
            LastRequest=request;
            if(request.SenderId!=300||request.ChatId!=200||!string.Equals(request.ChatType,"private",StringComparison.OrdinalIgnoreCase))return new(TelegramIngestionStatuses.Unauthorized);
            if(request.Photo is {Count:>0})
            {
                var photo=TelegramInboxMapping.SelectPhoto(request.Photo)!;await telegram.DownloadFileAsync(photo.FileId,1024,cancellationToken);return new(TelegramIngestionStatuses.Created,Guid.NewGuid(),"MANUAL");
            }
            var content=TelegramInboxMapping.ExtractContent(request.Text,request.Caption);
            if(request.MessageId is null or <=0||content is null){await Acknowledge(TelegramAcknowledgements.Unsupported,cancellationToken);return new(TelegramIngestionStatuses.Unsupported);}
            var key=TelegramInboxMapping.BuildIngestionKey(200,request.MessageId.Value);RawJobPosting? existing;
            lock(gate)existing=rows.SingleOrDefault(item=>item.IngestionKey==key);
            if(existing is not null)return new(TelegramIngestionStatuses.AlreadyReceived,existing.Id,existing.Source);
            var url=TelegramInboxMapping.SelectSourceUrl(content);var source=TelegramInboxMapping.ClassifySource(url);var now=DateTimeOffset.UtcNow;
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var row=new RawJobPosting{Id=Guid.NewGuid(),Source=source,IngestionKey=key,SourceUrl=url,RawContent=content,ContentHash=hash,IngestionStatus="RECEIVED",Metadata=System.Text.Json.JsonDocument.Parse("{}"),DiscoveredAt=now,CreatedAt=now,UpdatedAt=now};
            lock(gate)rows.Add(row);
            await Acknowledge(TelegramAcknowledgements.Received(source),cancellationToken);return new(TelegramIngestionStatuses.Created,row.Id,source);
        }
        private async Task Acknowledge(string text,CancellationToken cancellationToken){try{await telegram.SendMessageAsync(200,text,cancellationToken);}catch(HttpRequestException){}}
    }
}
