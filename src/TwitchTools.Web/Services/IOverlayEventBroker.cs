namespace TwitchTools.Web.Services;

public interface IOverlayEventBroker
{
    IAsyncEnumerable<BrokerEvent> SubscribeAsync(string overlayToken, CancellationToken cancellationToken);
    Task PublishAsync(string overlayToken, string payload, CancellationToken cancellationToken);
}

/// <summary>
/// A published payload. Sequence increases by one per publish for the key, including publishes no
/// subscriber received, so a subscriber that sees a jump knows how many it missed.
/// </summary>
public sealed record BrokerEvent(long Sequence, string Payload);
