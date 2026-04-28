using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class ViewerMonitoringService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IOptions<TwitchOptions> twitchOptions,
    ILogger<ViewerMonitoringService> logger) : IViewerMonitoringService
{
    public async Task PollViewerDurationsAsync(CancellationToken cancellationToken)
    {
        var streamers = await dbContext.Streamers.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var streamer in streamers)
        {
            var auth = BuildAuth(streamer, twitchOptions.Value);
            if (auth is null)
            {
                logger.LogWarning("Skipping viewer monitoring for {Streamer} because Twitch auth is not configured.", streamer.DisplayName);
                continue;
            }

            var isLive = await twitchApiClient.IsStreamerLiveAsync(streamer.TwitchUserId, auth, cancellationToken);
            if (!isLive)
            {
                continue;
            }

            var chatterIds = await twitchApiClient.GetCurrentChattersAsync(streamer.TwitchUserId, auth, cancellationToken);
            if (chatterIds.Count == 0)
            {
                continue;
            }

            var latestByViewer = await dbContext.ViewerDurationSamples
                .Where(x => x.StreamerId == streamer.Id)
                .GroupBy(x => x.TwitchViewerId)
                .Select(g => g.OrderByDescending(x => x.CapturedUtc).First())
                .ToDictionaryAsync(x => x.TwitchViewerId, x => x, cancellationToken);

            foreach (var chatterId in chatterIds)
            {
                var previousSeconds = latestByViewer.TryGetValue(chatterId, out var existing)
                    ? existing.TotalSecondsWatched
                    : 0;

                dbContext.ViewerDurationSamples.Add(new ViewerDurationSample
                {
                    StreamerId = streamer.Id,
                    TwitchViewerId = chatterId,
                    TotalSecondsWatched = previousSeconds + 60,
                    CapturedUtc = DateTime.UtcNow
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Viewer duration polling cycle complete.");
    }

    private static TwitchAuthContext? BuildAuth(Streamer streamer, TwitchOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        var tokenToUse = string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken)
            ? streamer.TwitchStreamerAccessToken
            : streamer.TwitchBotAccessToken;
        var moderatorUserId = !string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken)
            && !string.IsNullOrWhiteSpace(streamer.TwitchBotUserId)
            ? streamer.TwitchBotUserId
            : streamer.TwitchUserId;

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(tokenToUse)
            || string.IsNullOrWhiteSpace(moderatorUserId))
        {
            return null;
        }

        return new TwitchAuthContext(
            clientId,
            tokenToUse,
            moderatorUserId,
            moderatorUserId);
    }
}