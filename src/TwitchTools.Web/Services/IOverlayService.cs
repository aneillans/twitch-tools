using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public interface IOverlayService
{
    Task<OverlayWidgetViewModel?> GetFollowerByTokenAsync(string overlayToken, CancellationToken cancellationToken);
    Task<OverlayWidgetViewModel?> GetSubscriberByTokenAsync(string overlayToken, CancellationToken cancellationToken);
    Task<CustomOverlayWidgetRuntimeViewModel?> GetCustomWidgetByTokenAsync(string overlayToken, CancellationToken cancellationToken);
    Task SaveCustomWidgetAsync(string ownerSubject, CustomOverlayWidgetInput input, CancellationToken cancellationToken);
}