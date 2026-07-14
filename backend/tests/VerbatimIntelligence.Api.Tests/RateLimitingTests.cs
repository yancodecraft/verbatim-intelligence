using System.Net;

using Microsoft.AspNetCore.Http;

using VerbatimIntelligence.Api;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The rate-limit partition key is the client IP (the real caller once
/// UseForwardedHeaders has run behind the proxy), so a window is scoped per
/// client instead of being shared by everyone.
/// </summary>
public sealed class RateLimitingTests
{
    [Fact]
    public void ClientPartitionKey_UsesTheRemoteIpAddress()
    {
        var one = new DefaultHttpContext();
        one.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var another = new DefaultHttpContext();
        another.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");

        Assert.Equal("203.0.113.7", RateLimiting.ClientPartitionKey(one));
        Assert.NotEqual(
            RateLimiting.ClientPartitionKey(one),
            RateLimiting.ClientPartitionKey(another));
    }

    [Fact]
    public void ClientPartitionKey_FallsBackWhenTheIpIsUnknown()
    {
        var context = new DefaultHttpContext();

        Assert.Equal("unknown", RateLimiting.ClientPartitionKey(context));
    }
}