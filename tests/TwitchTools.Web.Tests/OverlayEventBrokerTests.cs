using System.Diagnostics.Metrics;
using FluentAssertions;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Tests;

public sealed class OverlayEventBrokerTests
{
    [Fact]
    public async Task Subscriber_receives_payloads_with_increasing_sequence()
    {
        var broker = new OverlayEventBroker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = broker.SubscribeAsync("key", cts.Token).GetAsyncEnumerator(cts.Token);
        var first = events.MoveNextAsync().AsTask();

        await broker.PublishAsync("key", "a", cts.Token);
        await broker.PublishAsync("key", "b", cts.Token);

        (await first).Should().BeTrue();
        events.Current.Should().Be(new BrokerEvent(1, "a"));
        (await events.MoveNextAsync()).Should().BeTrue();
        events.Current.Should().Be(new BrokerEvent(2, "b"));
    }

    [Fact]
    public async Task Sequence_advances_while_nobody_is_subscribed_so_subscribers_can_detect_the_gap()
    {
        var broker = new OverlayEventBroker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await broker.PublishAsync("key", "missed-1", cts.Token);
        await broker.PublishAsync("key", "missed-2", cts.Token);

        await using var events = broker.SubscribeAsync("key", cts.Token).GetAsyncEnumerator(cts.Token);
        var next = events.MoveNextAsync().AsTask();
        await broker.PublishAsync("key", "seen", cts.Token);

        (await next).Should().BeTrue();
        events.Current.Should().Be(new BrokerEvent(3, "seen"));
    }

    [Fact]
    public async Task Sequences_are_independent_per_key()
    {
        var broker = new OverlayEventBroker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await broker.PublishAsync("other", "x", cts.Token);
        await broker.PublishAsync("other", "y", cts.Token);

        await using var events = broker.SubscribeAsync("key", cts.Token).GetAsyncEnumerator(cts.Token);
        var next = events.MoveNextAsync().AsTask();
        await broker.PublishAsync("key", "a", cts.Token);

        (await next).Should().BeTrue();
        events.Current.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task Publish_records_whether_any_subscriber_received_it()
    {
        const string brokerName = "test-delivered";
        var broker = new OverlayEventBroker(brokerName);
        var measurements = new List<bool>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OverlayEventBroker.MeterName && instrument.Name == "twitchtools.broker.events_published")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
            if (Equals(values["broker"], brokerName))
            {
                lock (measurements)
                {
                    measurements.Add((bool)values["delivered"]!);
                }
            }
        });
        listener.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await broker.PublishAsync("key", "nobody", cts.Token);

        await using var events = broker.SubscribeAsync("key", cts.Token).GetAsyncEnumerator(cts.Token);
        var next = events.MoveNextAsync().AsTask();
        await broker.PublishAsync("key", "someone", cts.Token);
        await next;

        measurements.Should().Equal(false, true);
    }
}
