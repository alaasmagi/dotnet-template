using DataAccess.Context;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Web.Configuration;

/// <summary>
/// Reports whether the application database is reachable.
/// </summary>
public class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database is not reachable.");
    }
}
