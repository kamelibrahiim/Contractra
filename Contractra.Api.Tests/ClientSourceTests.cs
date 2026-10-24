using System.Net;
using Contractra.Api.Http;

namespace Contractra.Api.Tests;

/// <summary>Contract §22 E: the throttle source for layers 2-3 groups one IPv6 /64 as a single source.</summary>
public class ClientSourceTests
{
    private static DefaultHttpContext ContextWith(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return context;
    }

    [Fact]
    public void ThrottleGroupOf_TwoAddressesInTheSameIPv6Slash64_MapToTheSameGroup()
    {
        var first = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:1234:5678::1"));
        var second = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:1234:5678:ffff:ffff:ffff:ffff"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ThrottleGroupOf_TwoAddressesInDifferentIPv6Slash64Networks_MapToDifferentGroups()
    {
        var first = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:1234:5678::1"));
        var second = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:1234:5679::1"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ThrottleGroupOf_TwoDifferentIPv4Addresses_MapToDifferentGroups()
    {
        var first = ClientSource.ThrottleGroupOf(ContextWith("203.0.113.5"));
        var second = ClientSource.ThrottleGroupOf(ContextWith("203.0.113.6"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ThrottleGroupOf_TheSameIPv4AddressTwice_MapsToTheSameGroup()
    {
        var first = ClientSource.ThrottleGroupOf(ContextWith("203.0.113.5"));
        var second = ClientSource.ThrottleGroupOf(ContextWith("203.0.113.5"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ThrottleGroupOf_AnIPv4MappedIPv6Address_GroupsWithTheSamePlainIPv4Address()
    {
        var mapped = ClientSource.ThrottleGroupOf(ContextWith("::ffff:203.0.113.5"));
        var plain = ClientSource.ThrottleGroupOf(ContextWith("203.0.113.5"));

        Assert.Equal(plain, mapped);
    }

    [Fact]
    public void ThrottleGroupOf_NoRemoteAddress_ReturnsUnknown()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        Assert.Equal(ClientSource.Unknown, ClientSource.ThrottleGroupOf(context));
    }

    [Fact]
    public void Of_StillReturnsTheExactAddress_UnaffectedByGrouping()
    {
        Assert.Equal("2001:db8:1234:5678::1", ClientSource.Of(ContextWith("2001:db8:1234:5678::1")));
        Assert.Equal("2001:db8:1234:5679::1", ClientSource.Of(ContextWith("2001:db8:1234:5679::1")));
    }

    // ThrottleGroupOf and Of both only ever read Connection.RemoteIpAddress, so
    // whether that value came from the raw TCP connection or was already
    // rewritten by ForwardedHeadersMiddleware from a trusted proxy's
    // X-Forwarded-For (contract §17's trusted-proxy rule, enforced upstream of
    // this class) makes no difference here: grouping applies the same way to
    // whatever address is already on the context by the time this runs.
    [Fact]
    public void ThrottleGroupOf_OnAnAddressAlreadyResolvedByTrustedProxyForwarding_StillGroupsByTheSameSlash64()
    {
        // Simulates the context state after ForwardedHeadersMiddleware has
        // replaced the proxy's own address with the forwarded client address.
        var forwarded = ContextWith("2001:db8:cafe:beef::42");

        var first = ClientSource.ThrottleGroupOf(forwarded);
        var second = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:cafe:beef:ffff:ffff:ffff:ffff"));
        var differentNetwork = ClientSource.ThrottleGroupOf(ContextWith("2001:db8:cafe:beee::42"));

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentNetwork);
    }
}
