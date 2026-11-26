using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Web.Configuration;

namespace Tests;

public class ApiRateLimitPartitionTests
{
    private static HttpContext Context(ClaimsIdentity identity)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return context;
    }

    [Test]
    public void KeyFor_AuthenticatedWithSub_UsesSub()
    {
        var context = Context(new ClaimsIdentity([new Claim("sub", "user-1")], "Bearer"));

        Assert.That(ApiRateLimitPartition.KeyFor(context), Is.EqualTo("sub:user-1"));
    }

    [Test]
    public void KeyFor_Anonymous_UsesRemoteIp()
    {
        var context = Context(new ClaimsIdentity());

        Assert.That(ApiRateLimitPartition.KeyFor(context), Is.EqualTo("ip:203.0.113.7"));
    }

    [Test]
    public void KeyFor_UnauthenticatedIdentityWithSub_UsesRemoteIp()
    {
        var context = Context(new ClaimsIdentity([new Claim("sub", "user-1")]));

        Assert.That(ApiRateLimitPartition.KeyFor(context), Is.EqualTo("ip:203.0.113.7"));
    }

    [Test]
    public void KeyFor_AuthenticatedWithoutSub_UsesRemoteIp()
    {
        var context = Context(new ClaimsIdentity([new Claim("name", "user")], "Cookies"));

        Assert.That(ApiRateLimitPartition.KeyFor(context), Is.EqualTo("ip:203.0.113.7"));
    }
}
