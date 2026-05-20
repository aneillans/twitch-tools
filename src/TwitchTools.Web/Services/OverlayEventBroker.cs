using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TwitchTools.Web.Services;

public sealed class OverlayEventBroker : IOverlayEventBroker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _subscribers = new(StringComparer.Ordinal);

    public async IAsyncEnumerable<string> SubscribeAsync(
        string overlayToken,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var tokenSubscriptions = _subscribers.GetOrAdd(overlayToken, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        var subscriptionId = Guid.NewGuid();
        tokenSubscriptions[subscriptionId] = channel;

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }
        finally
        {
            tokenSubscriptions.TryRemove(subscriptionId, out _);
            if (tokenSubscriptions.IsEmpty)
            {
                _subscribers.TryRemove(overlayToken, out _);
            }
        }
    }

    public Task PublishAsync(string overlayToken, string payload, CancellationToken cancellationToken)
    {
        if (!_subscribers.TryGetValue(overlayToken, out var tokenSubscriptions))
        {
            return Task.CompletedTask;
        }

        foreach (var subscription in tokenSubscriptions.Values)
        {
            _ = subscription.Writer.TryWrite(payload);
        }

        return Task.CompletedTask;
    }
}
