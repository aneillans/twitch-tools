namespace TwitchTools.Web.Services;

public interface IViewerMonitoringService
{
    Task PollViewerDurationsAsync(CancellationToken cancellationToken);
}