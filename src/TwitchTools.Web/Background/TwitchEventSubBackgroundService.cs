using TwitchTools.Web.Services;

namespace TwitchTools.Web.Background;

public sealed class TwitchEventSubBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TwitchEventSubBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var eventSubService = scope.ServiceProvider.GetRequiredService<ITwitchEventSubService>();
                await eventSubService.EnsureSubscriberSubscriptionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EventSub subscription bootstrap cycle failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
