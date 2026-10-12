namespace TwitchTools.Web.Services;

/// <summary>
/// Live Twitch + YouTube chat for the streamer's portal chat window. Messages are published to a
/// separate <see cref="IOverlayEventBroker"/> instance (keyed <see cref="BrokerKey"/>) keyed by
/// streamer id, so they never reach the anonymous overlay event stream.
/// </summary>
public static class StreamerChatFeed
{
    public const string BrokerKey = "streamer-chat";

    public static string Key(Guid streamerId) => streamerId.ToString("N");
}

public sealed record StreamerChatMessage(
    string Platform,
    string MessageId,
    string UserId,
    string? UserLogin,
    string DisplayName,
    string Text,
    IReadOnlyList<StreamerChatFragment> Fragments,
    DateTimeOffset SentUtc);

/// <summary>A run of message text; EmoteId is set for Twitch emotes so the page can show the image.</summary>
public sealed record StreamerChatFragment(string Text, string? EmoteId);
