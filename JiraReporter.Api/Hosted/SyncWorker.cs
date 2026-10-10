using JiraReporter.Api.Services;

namespace JiraReporter.Api.Hosted;

public sealed class SyncWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = GetSyncInterval();

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSyncAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private TimeSpan GetSyncInterval()
    {
        var hours = double.TryParse(configuration["Jira:SyncIntervalHours"], out var configuredHours) &&
            configuredHours > 0
                ? configuredHours
                : 6;

        return TimeSpan.FromHours(hours);
    }

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<JiraSyncService>();
            await syncService.SyncAllAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; cancellation is expected.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Jira sync failed");
        }
    }
}
