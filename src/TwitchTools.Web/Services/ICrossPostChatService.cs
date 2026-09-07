using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

/// <summary>
/// Mirrors a chat message that arrived on one platform onto the other platform's chat, using each
/// streamer's configured bot account. Callers are responsible for first excluding messages authored
/// by the streamer's own bot identity (see <see cref="Streamer.TwitchBotUserId"/> /
/// <see cref="Streamer.YouTubeBotChannelId"/>) so mirrored messages are never mirrored again.
/// </summary>
public interface ICrossPostChatService
{
    Task CrossPostFromTwitchAsync(Streamer streamer, string authorDisplayName, string messageText, CancellationToken cancellationToken);

    Task CrossPostFromYouTubeAsync(Streamer streamer, string authorDisplayName, string messageText, CancellationToken cancellationToken);
}
