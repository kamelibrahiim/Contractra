using System.Net;

namespace Contractra.Api.Http;

/// <summary>
/// The client IP of contract §17: the connection's remote address, which the
/// ForwardedHeaders middleware replaces with <c>X-Forwarded-For</c> only when the request
/// came through a configured trusted proxy (explicit KnownProxies / KnownIPNetworks;
/// the framework's loopback defaults are cleared). A client-supplied X-Forwarded-For is
/// otherwise ignored.
/// </summary>
public static class ClientSource
{
    public const string Unknown = "unknown";

    public static string Of(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return Unknown;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    /// <summary>
    /// The §22 E throttle source (layers 2-3 only, over the shared store): one
    /// IPv4 address, or one IPv6 /64 network - computed from address bytes via
    /// <see cref="IPNetwork"/>, never by string truncation, so a single /64 host
    /// rotating addresses cannot bypass those layers (contract §22 E, OC-8).
    /// Layer 1/5's in-memory limiter is unaffected: it keeps the exact address
    /// from <see cref="Of"/>, consistent with §17's "never the only defence".
    /// </summary>
    public static string ThrottleGroupOf(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return new IPNetwork(address, 64).ToString();
        }

        return address.ToString();
    }

    public static void ConfigureForwardedHeaders(Microsoft.AspNetCore.Builder.ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies contains a value that is not an IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException("ForwardedHeaders:KnownNetworks contains a value that is not a CIDR network.");
            }

            if (parsed.PrefixLength == 0)
            {
                throw new InvalidOperationException("ForwardedHeaders:KnownNetworks must not trust every address (contract §18).");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }
}
