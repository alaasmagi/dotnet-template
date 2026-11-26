using Base.Message.RabbitMQ;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Web.Configuration;

/// <summary>
/// Reports whether the shared RabbitMQ connection is open.
/// </summary>
public class RabbitMqHealthCheck(RabbitMqConnectionManager connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // A failed connect is the expected unhealthy outcome, not an error to propagate.
        try
        {
            var connection = await connections.GetConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The RabbitMQ connection is closed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", exception);
        }
    }
}
