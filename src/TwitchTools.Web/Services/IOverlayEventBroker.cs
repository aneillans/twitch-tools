namespace TwitchTools.Web.Services;

public interface IOverlayEventBroker
{
    IAsyncEnumerable<string> SubscribeAsync(string overlayToken, CancellationToken cancellationToken);
    Task PublishAsync(string overlayToken, string payload, CancellationToken cancellationToken);
}
