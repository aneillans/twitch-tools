using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TwitchTools.Web.Services;

public sealed class OverlayEventBroker(string name = OverlayEventBroker.DefaultName) : IOverlayEventBroker
{
    public const string DefaultName = "overlay";
    public const string MeterName = "TwitchTools.EventBroker";

    private static readonly Meter Meter = new(MeterName);

    // delivered=false means no subscriber was connected, so the event reached nobody.
    private static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "twitchtools.broker.events_published", description: "Events published, tagged by whether any subscriber was connected.");

    private static readonly UpDownCounter<long> Subscribers = Meter.CreateUpDownCounter<long>(
        "twitchtools.broker.subscribers", description: "Currently connected subscribers.");

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<BrokerEvent>>> _subscribers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StrongBox<long>> _sequences = new(StringComparer.Ordinal);

    public async IAsyncEnumerable<BrokerEvent> SubscribeAsync(
        string overlayToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<BrokerEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var tokenSubscriptions = _subscribers.GetOrAdd(overlayToken, _ => new ConcurrentDictionary<Guid, Channel<BrokerEvent>>());
        var subscriptionId = Guid.NewGuid();
        tokenSubscriptions[subscriptionId] = channel;
        Subscribers.Add(1, new KeyValuePair<string, object?>("broker", name));

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }
        finally
        {
            Subscribers.Add(-1, new KeyValuePair<string, object?>("broker", name));
            tokenSubscriptions.TryRemove(subscriptionId, out _);
            if (tokenSubscriptions.IsEmpty)
            {
                _subscribers.TryRemove(overlayToken, out _);
            }
        }
    }

    public Task PublishAsync(string overlayToken, string payload, CancellationToken cancellationToken)
    {
        var sequence = Interlocked.Increment(ref _sequences.GetOrAdd(overlayToken, _ => new StrongBox<long>()).Value);
        var message = new BrokerEvent(sequence, payload);

        var delivered = false;
        if (_subscribers.TryGetValue(overlayToken, out var tokenSubscriptions))
        {
            foreach (var subscription in tokenSubscriptions.Values)
            {
                delivered |= subscription.Writer.TryWrite(message);
            }
        }

        Published.Add(1, new KeyValuePair<string, object?>("broker", name), new KeyValuePair<string, object?>("delivered", delivered));
        Activity.Current?.AddEvent(new ActivityEvent("broker.published", tags: new ActivityTagsCollection
        {
            ["broker"] = name,
            ["sequence"] = sequence,
            ["delivered"] = delivered
        }));

        return Task.CompletedTask;
    }
}
