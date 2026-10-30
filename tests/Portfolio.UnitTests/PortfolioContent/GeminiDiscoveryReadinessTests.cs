using Microsoft.Extensions.Options;
using Portfolio.Infrastructure.AI;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class GeminiDiscoveryReadinessTests
{
    [Theory] [InlineData("", "synthetic-model")] [InlineData("synthetic-key", "")] [InlineData("synthetic-key", "unconfigured")] [InlineData("synthetic-key", "../unsafe/model")]
    public async Task Missing_or_invalid_configuration_makes_zero_HTTP_calls(string key,string model)
    {
        using var http=new HttpClient(new NoNetwork());
        var extractor=new GeminiDiscoveryTextExtractor(http,Options.Create(new GeminiSettings{ApiKey=key,JobExtractionModel=model}));
        Assert.False(extractor.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>extractor.ExtractAsync("Synthetic original content",default));
    }
    private sealed class NoNetwork:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new Xunit.Sdk.XunitException("NETWORK_FORBIDDEN");
    }
}
