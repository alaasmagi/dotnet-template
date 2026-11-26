using Base.Message.RabbitMQ;
using DataAccess.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Web.Configuration;

namespace Tests;

public class HealthCheckTests
{
    // Nothing listens on port 1, so both connections are refused immediately.
    private const string UnreachableHost = "127.0.0.1";
    private const int UnreachablePort = 1;

    [Test]
    public async Task DatabaseHealthCheck_UnreachableDatabase_IsUnhealthy()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"Host={UnreachableHost};Port={UnreachablePort};Database=app;Username=app;Password=app;Timeout=2")
            .Options;
        await using var dbContext = new AppDbContext(options);

        var result = await new DatabaseHealthCheck(dbContext).CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task RabbitMqHealthCheck_UnreachableBroker_IsUnhealthy()
    {
        await using var connections = new RabbitMqConnectionManager(
            new RabbitMqOptions
            {
                Host = UnreachableHost,
                Port = UnreachablePort,
                Username = "guest",
                Password = "guest",
                VirtualHost = "/",
                Exchange = "app"
            },
            NullLogger<RabbitMqConnectionManager>.Instance);

        var result = await new RabbitMqHealthCheck(connections).CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }
}
