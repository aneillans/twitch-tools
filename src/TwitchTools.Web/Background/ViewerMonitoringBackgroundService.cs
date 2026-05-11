using Exceptionless;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Background;

public sealed class ViewerMonitoringBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ViewerMonitoringBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var monitorService = scope.ServiceProvider.GetRequiredService<IViewerMonitoringService>();
                await monitorService.PollViewerDurationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Viewer monitoring cycle failed.");
                ExceptionlessClient.Default.SubmitException(ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}