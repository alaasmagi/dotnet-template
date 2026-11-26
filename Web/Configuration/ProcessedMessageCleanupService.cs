using Contracts.Application;

namespace Web.Configuration;

/// <summary>
/// Deletes processed message ids older than the retention period once a day.
/// </summary>
public class ProcessedMessageCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessedMessageCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            // A failed run is reported and retried on the next tick instead of stopping the service.
            try
            {
                using var scope = scopeFactory.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<IProcessedMessageService>().RemoveExpiredAsync();
                if (!result.Successful)
                {
                    logger.LogError("Processed message cleanup failed: {ErrorCode} {ErrorMessage}", result.Error?.Code, result.Error?.Message);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Processed message cleanup failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
