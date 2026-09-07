using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

public interface IYouTubeViewerMonitoringService
{
    /// <summary>
    /// Records that a viewer's chat message was observed during a live broadcast, extending (or
    /// starting) their watch-time session for that broadcast. See
    /// <see cref="YouTubeViewerDurationSample"/> for the accumulation model.
    /// </summary>
    Task RecordViewerActivityAsync(
        Streamer streamer,
        string videoId,
        string authorChannelId,
        string? authorDisplayName,
        DateTimeOffset messageTimestampUtc,
        CancellationToken cancellationToken);
}
