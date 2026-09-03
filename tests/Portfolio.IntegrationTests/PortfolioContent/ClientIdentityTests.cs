using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Api.ChatProtection;
using Portfolio.Api.Startup;
using Portfolio.Infrastructure;
using Portfolio.Infrastructure.ChatProtection;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class ClientIdentityTests
{
    [Theory]
    [InlineData("192.0.2.15", "192.0.2.15")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("::ffff:192.0.2.15", "192.0.2.15")]
    public void Ip_normalization_is_canonical(string input, string expected) =>
        Assert.Equal(expected, ClientIdentityProvider.Normalize(IPAddress.Parse(input)));

    [Fact]
    public void Hmac_identity_is_deterministic_private_and_secret_specific()
    {
        var first = Provider("first-test-secret-with-at-least-32-characters");
        var same = Provider("first-test-secret-with-at-least-32-characters");
        var other = Provider("other-test-secret-with-at-least-32-characters");
        var ipv4 = Context("192.0.2.15");
        var mapped = Context("::ffff:192.0.2.15");

        var key = first.GetVisitorKey(ipv4);

        Assert.Equal(key, same.GetVisitorKey(ipv4));
        Assert.Equal(key, first.GetVisitorKey(mapped));
        Assert.NotEqual(key, first.GetVisitorKey(Context("192.0.2.16")));
        Assert.NotEqual(key, other.GetVisitorKey(ipv4));
        Assert.StartsWith("ip:", key);
        Assert.DoesNotContain("192.0.2.15", key, StringComparison.Ordinal);
        Assert.DoesNotContain("first-test-secret", key, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public async Task Missing_or_weak_hmac_secret_fails_host_startup(string? secret)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = "Host=localhost;Database=unused",
                    ["Jwt:Issuer"] = "tests",
                    ["Jwt:Audience"] = "tests",
                    ["Jwt:SecretKey"] = "test-jwt-secret-with-at-least-32-characters",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["ChatProtection:IpHashSecret"] = secret,
                    ["Gemini:EnableIndexingWorker"] = "false"
                }))
            .ConfigureServices((context, services) => services.AddInfrastructure(context.Configuration))
            .Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("IpHashSecret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-jwt-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forwarded_headers_only_add_explicit_trusted_proxy_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Proxy:ForwardLimit"] = "2",
                ["Proxy:KnownProxies:0"] = "192.0.2.10",
                ["Proxy:KnownNetworks:0"] = "2001:db8::/32"
            }).Build();
        var services = new ServiceCollection();
        services.AddTrustedForwardedHeaders(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Equal(2, options.ForwardLimit);
        Assert.Contains(IPAddress.Parse("192.0.2.10"), options.KnownProxies);
        Assert.Contains(options.KnownNetworks, network =>
            network.Prefix.Equals(IPAddress.Parse("2001:db8::")) && network.PrefixLength == 32);
    }

    private static ClientIdentityProvider Provider(string secret) =>
        new(Options.Create(new ChatProtectionSettings { IpHashSecret = secret }));

    private static DefaultHttpContext Context(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }
}
