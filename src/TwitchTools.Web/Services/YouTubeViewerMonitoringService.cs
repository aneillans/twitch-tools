using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

public sealed class YouTubeViewerMonitoringService(AppDbContext dbContext) : IYouTubeViewerMonitoringService
{
    public async Task RecordViewerActivityAsync(
        Streamer streamer,
        string videoId,
        string authorChannelId,
        string? authorDisplayName,
        DateTimeOffset messageTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(authorChannelId))
        {
            return;
        }

        var messageTimeUtc = messageTimestampUtc.UtcDateTime;

        var latest = await dbContext.YouTubeViewerDurationSamples
            .Where(x => x.StreamerId == streamer.Id && x.YouTubeViewerId == authorChannelId)
            .OrderByDescending(x => x.CapturedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null || !string.Equals(latest.VideoId, videoId, StringComparison.Ordinal))
        {
            // First time we've seen this viewer, or they're back under a new broadcast - start a
            // fresh session row, carrying their prior all-time total forward as the base.
            var sessionBaseSeconds = latest?.TotalSecondsWatched ?? 0;
            dbContext.YouTubeViewerDurationSamples.Add(new YouTubeViewerDurationSample
            {
                StreamerId = streamer.Id,
                YouTubeViewerId = authorChannelId,
                YouTubeViewerDisplayName = authorDisplayName,
                VideoId = videoId,
                SessionBaseSeconds = sessionBaseSeconds,
                FirstSeenUtc = messageTimeUtc,
                LastSeenUtc = messageTimeUtc,
                TotalSecondsWatched = sessionBaseSeconds,
                CapturedUtc = DateTime.UtcNow
            });
        }
        else
        {
            // Still within the same broadcast - extend the session span in place.
            if (messageTimeUtc > latest.LastSeenUtc)
            {
                latest.LastSeenUtc = messageTimeUtc;
            }

            latest.TotalSecondsWatched = latest.SessionBaseSeconds + (int)Math.Max(0, (latest.LastSeenUtc - latest.FirstSeenUtc).TotalSeconds);
            latest.CapturedUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(authorDisplayName))
            {
                latest.YouTubeViewerDisplayName = authorDisplayName;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
