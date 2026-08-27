using System.Threading.Channels;

namespace TwitchTools.Web.Services;

/// <summary>
/// Raw payload for a stream.online/stream.offline EventSub notification that has passed
/// signature validation and duplicate-message checks, awaiting background processing.
/// </summary>
public sealed record StreamStatusEventWorkItem(bool IsLive, string RawEventJson, string MessageId);

/// <summary>
/// Hands stream status EventSub notifications off to a background worker so the webhook
/// callback can acknowledge Twitch immediately instead of performing slow downstream work
/// (Twitch API calls, Discord sync, BlueSky posting) inline with the HTTP request.
/// </summary>
public interface IEventSubStreamStatusDispatcher
{
    void Enqueue(StreamStatusEventWorkItem workItem);

    IAsyncEnumerable<StreamStatusEventWorkItem> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class EventSubStreamStatusDispatcher : IEventSubStreamStatusDispatcher
{
    private readonly Channel<StreamStatusEventWorkItem> _channel = Channel.CreateUnbounded<StreamStatusEventWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public void Enqueue(StreamStatusEventWorkItem workItem) => _channel.Writer.TryWrite(workItem);

    public IAsyncEnumerable<StreamStatusEventWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
