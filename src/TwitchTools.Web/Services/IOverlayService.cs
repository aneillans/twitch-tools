using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public interface IOverlayService
{
    Task<OverlayWidgetViewModel?> GetFollowerByTokenAsync(string overlayToken, CancellationToken cancellationToken);
    Task<OverlayWidgetViewModel?> GetSubscriberByTokenAsync(string overlayToken, CancellationToken cancellationToken);
}