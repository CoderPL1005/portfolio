using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ForwardedIpNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Portfolio.Api.Startup;

public sealed class ProxySettings
{
    public const string SectionName = "Proxy";
    public int ForwardLimit { get; init; } = 1;
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
}

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ProxySettings>()
            .Bind(configuration.GetSection(ProxySettings.SectionName))
            .Validate(settings => settings.ForwardLimit is > 0 and <= 10,
                "Proxy:ForwardLimit must be between 1 and 10.")
            .Validate(settings => settings.KnownProxies.All(value => IPAddress.TryParse(value, out _)),
                "Proxy:KnownProxies contains an invalid IP address.")
            .Validate(settings => settings.KnownNetworks.All(IsValidNetwork),
                "Proxy:KnownNetworks contains an invalid CIDR network.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ProxySettings>>((options, configured) =>
        {
            var settings = configured.Value;
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;
            foreach (var proxy in settings.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in settings.KnownNetworks)
            {
                var parts = network.Split('/', 2);
                options.KnownNetworks.Add(new ForwardedIpNetwork(
                    IPAddress.Parse(parts[0]),
                    int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
            }
        });
        return services;
    }

    private static bool IsValidNetwork(string value)
    {
        var parts = value.Split('/', 2);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], out var prefix))
        {
            return false;
        }

        return prefix >= 0 && prefix <= (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
    }
}
