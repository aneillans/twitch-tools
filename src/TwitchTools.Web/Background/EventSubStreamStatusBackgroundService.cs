using TwitchTools.Web.Services;

namespace TwitchTools.Web.Background;

/// <summary>
/// Consumes stream status EventSub notifications queued by <see cref="TwitchEventSubService"/> and
/// processes them outside of the webhook HTTP request/response cycle. Each item is processed inside
/// its own error boundary so that a failure handling one notification (e.g. a transient database or
/// BlueSky outage) cannot crash the worker or block subsequent notifications.
/// </summary>
public sealed class EventSubStreamStatusBackgroundService(
    IEventSubStreamStatusDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<EventSubStreamStatusBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in dispatcher.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var eventSubService = scope.ServiceProvider.GetRequiredService<ITwitchEventSubService>();
                await eventSubService.ProcessQueuedStreamStatusEventAsync(workItem, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to process queued {EventType} EventSub notification. MessageId={MessageId}",
                    workItem.IsLive ? "stream.online" : "stream.offline",
                    workItem.MessageId);
            }
        }
    }
}
