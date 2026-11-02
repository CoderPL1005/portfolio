using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Infrastructure.AI;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class DiscoveryGeminiHttpFailureTests(AuthApiFactory root):IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData(400,"INVALID_ARGUMENT","INVALID_ARGUMENT")]
    [InlineData(400,"FAILED_PRECONDITION","FAILED_PRECONDITION")]
    [InlineData(401,"UNAUTHENTICATED","UNAUTHENTICATED")]
    [InlineData(403,"PERMISSION_DENIED","PERMISSION_DENIED")]
    [InlineData(404,"NOT_FOUND","NOT_FOUND")]
    [InlineData(429,"RESOURCE_EXHAUSTED","RESOURCE_EXHAUSTED")]
    [InlineData(500,"INTERNAL","INTERNAL")]
    [InlineData(503,"UNAVAILABLE","UNAVAILABLE")]
    public async Task Actual_adapter_HTTP_failure_keeps_UNKNOWN_accounting_and_safe_audit_without_a_retry(int statusCode,string providerStatus,string expectedCategory)
    {
        using var s=await RecoveryScenario.Create(root,1);var item=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var transport=new Reject(statusCode,providerStatus);using var http=new HttpClient(transport);
            var extractor=new GeminiDiscoveryTextExtractor(http,Options.Create(new GeminiSettings{ApiKey="SYNTHETIC_SECRET",JobExtractionModel="gemini-3.1-flash-lite"}));
            var service=new DiscoveryExecutionService(db,new NoGitHub(),extractor,Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=true,GeminiFreeTierConfirmed=true}),new Actor(),TimeProvider.System);
            var e=await db.DiscoveryExecutions.SingleAsync();
            var review=item with {ContentValidity=item.ContentValidity with {Status="VALID_RECRUITMENT"}};
            await service.ProcessAsync(s.Id,new(0,item.ContentHash,review,[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            await service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default);
            var unknownVersion=e.Version;
            await Assert.ThrowsAsync<ConflictException>(()=>service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default));
            Assert.Equal(1,transport.Calls);Assert.Equal(1,e.SemanticAttempts);Assert.Equal(unknownVersion,e.Version);
            var observation=await db.DiscoveryExecutionObservations.SingleAsync();Assert.Equal("UNKNOWN",observation.SemanticState);Assert.Null(observation.SemanticOutputJson);
            Assert.Equal(item.RawContent,JsonDocument.Parse(observation.PayloadJson).RootElement.GetProperty("rawContent").GetString());
            Assert.NotNull(e.ActiveKey);Assert.Null(e.ResultHash);Assert.Empty(await db.JobPostings.ToArrayAsync());
            var attempt=await db.DiscoveryExecutionAttempts.SingleAsync();Assert.Equal(1,attempt.ActualRequests);Assert.Equal(1,attempt.CreditsDeducted);Assert.Equal(6,attempt.CreditsUsedAlternateHeader);
            var events=await db.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==s.Id&&x.State=="SEMANTIC_DIAGNOSTIC").ToArrayAsync();
            var diagnostic=JsonDocument.Parse(Assert.Single(events).Reason!).RootElement;
            Assert.Equal(4,diagnostic.EnumerateObject().Count());Assert.Equal(statusCode,diagnostic.GetProperty("httpStatus").GetInt32());Assert.Equal("HTTP_RESPONSE",diagnostic.GetProperty("stage").GetString());Assert.Equal(expectedCategory,diagnostic.GetProperty("category").GetString());
            Assert.DoesNotContain("SYNTHETIC_SECRET",events[0].Reason!);Assert.DoesNotContain(item.RawContent!,events[0].Reason!);
        });s.AssertOffline();
    }
    [Fact]
    public async Task Adapter_timeout_commits_reservation_once_records_TIMEOUT_diagnostic_and_never_retries()
    {
        using var s=await RecoveryScenario.Create(root,1);var item=(await s.NormalResult()).ReviewBundle!.Items[0];
        await s.Read(async db=>{
            var transport=new TimeoutReject();using var http=new HttpClient(transport);
            var extractor=new GeminiDiscoveryTextExtractor(http,Options.Create(new GeminiSettings{ApiKey="SYNTHETIC_SECRET",JobExtractionModel="gemini-3.1-flash-lite"}));
            var service=new DiscoveryExecutionService(db,new NoGitHub(),extractor,Options.Create(new DiscoveryExecutionOptions{SemanticEnabled=true,GeminiFreeTierConfirmed=true}),new Actor(),TimeProvider.System);
            var e=await db.DiscoveryExecutions.SingleAsync();
            var review=item with {ContentValidity=item.ContentValidity with {Status="VALID_RECRUITMENT"}};
            await service.ProcessAsync(s.Id,new(0,item.ContentHash,review,[],ExpectedVersion:e.Version,ExpectedWorkflowRunId:"10001"),null,default);
            await service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default);
            await Assert.ThrowsAsync<ConflictException>(()=>service.ExtractPersistedAsync(s.Id,new(0,item.RawContent!,item.ContentHash!,e.Version,"10001",true),default));
            Assert.Equal(1,transport.Calls);Assert.Equal(1,e.SemanticAttempts);
            var observation=await db.DiscoveryExecutionObservations.SingleAsync();Assert.Equal("UNKNOWN",observation.SemanticState);
            var events=await db.DiscoveryExecutionEvents.Where(x=>x.ExecutionId==s.Id&&x.State=="SEMANTIC_DIAGNOSTIC").ToArrayAsync();
            var diagnostic=JsonDocument.Parse(Assert.Single(events).Reason!).RootElement;
            Assert.Equal("TIMEOUT",diagnostic.GetProperty("category").GetString());
            Assert.DoesNotContain("SYNTHETIC_SECRET",events[0].Reason!);
        });s.AssertOffline();
    }
    private sealed class Reject(int statusCode,string category):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode){Content=new StringContent(JsonSerializer.Serialize(new{error=new{status=category,message="SYNTHETIC_SECRET raw source is forbidden"}}))});}
    }
    private sealed class TimeoutReject:HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;throw new TaskCanceledException("SYNTHETIC_SECRET timeout");}
    }
    private sealed class NoGitHub:IDiscoveryGitHubClient
    {
        public Task<string> DispatchAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("NO_DISPATCH");
        public Task<bool> VerifyRunAsync(Guid id,string run,CancellationToken ct)=>throw new InvalidOperationException("NO_NETWORK");
    }
    private sealed class Actor:ICurrentUser{public bool IsAuthenticated=>true;public Guid? AdminUserId=>AuthApiFactory.AdminId;public string? Email=>null;}
}
