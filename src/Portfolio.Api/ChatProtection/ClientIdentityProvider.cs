using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Portfolio.Infrastructure.ChatProtection;

namespace Portfolio.Api.ChatProtection;

public interface IClientIdentityProvider
{
    string GetNormalizedIp(HttpContext context);
    string GetVisitorKey(HttpContext context);
}

public sealed class ClientIdentityProvider(
    IOptions<ChatProtectionSettings> options) : IClientIdentityProvider
{
    public string GetNormalizedIp(HttpContext context) =>
        Normalize(context.Connection.RemoteIpAddress);

    public string GetVisitorKey(HttpContext context)
    {
        var normalizedIp = GetNormalizedIp(context);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.IpHashSecret));
        return $"ip:{Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedIp))).ToLowerInvariant()}";
    }

    public static string Normalize(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString().ToLowerInvariant();
    }
}
