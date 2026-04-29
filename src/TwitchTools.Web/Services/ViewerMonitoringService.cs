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
            var overlayAuth = BuildOverlayAuth(streamer, twitchOptions.Value);
            if (overlayAuth is not null)
            {
                await UpdateOverlaySnapshotAsync(streamer, overlayAuth, cancellationToken);
            }

            var auth = BuildAuth(streamer, twitchOptions.Value);
            if (auth is null)
            {
                logger.LogWarning("Skipping viewer monitoring for {Streamer} because Twitch auth is not configured.", streamer.DisplayName);
                continue;
            }

            var isLive = await twitchApiClient.IsStreamerLiveAsync(streamer.TwitchUserId, auth, cancellationToken);
            if (!isLive)
            {
                logger.LogDebug("Skipping viewer sample collection for {Streamer} because the channel is not currently live.", streamer.DisplayName);
                continue;
            }

            var chatterIds = await twitchApiClient.GetCurrentChattersAsync(streamer.TwitchUserId, auth, cancellationToken);
            if (chatterIds.Count == 0)
            {
                logger.LogInformation(
                    "Viewer sample collection for {Streamer} returned zero chatters while live. BotUserId={BotUserId}.",
                    streamer.DisplayName,
                    auth.BotUserId);
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

            logger.LogInformation(
                "Recorded viewer samples for {Streamer}: {ViewerCount} active chatters.",
                streamer.DisplayName,
                chatterIds.Count);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Viewer duration polling cycle complete.");
    }

    private async Task UpdateOverlaySnapshotAsync(Streamer streamer, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        var latestFollower = await twitchApiClient.GetLatestFollowerAsync(streamer.TwitchUserId, authContext, cancellationToken);
        var latestSubscriber = await twitchApiClient.GetLatestSubscriberAsync(streamer.TwitchUserId, authContext, cancellationToken);

        if (latestFollower is null && latestSubscriber is null)
        {
            return;
        }

        var snapshot = await dbContext.OverlaySnapshots
            .FirstOrDefaultAsync(x => x.StreamerId == streamer.Id, cancellationToken);

        if (snapshot is null)
        {
            snapshot = new OverlaySnapshot
            {
                StreamerId = streamer.Id
            };
            dbContext.OverlaySnapshots.Add(snapshot);
        }

        if (latestFollower is not null)
        {
            snapshot.LastFollowerName = latestFollower.UserName ?? latestFollower.UserLogin ?? latestFollower.UserId;
            snapshot.LastFollowerUtc = latestFollower.FollowedAtUtc?.UtcDateTime ?? DateTime.UtcNow;
        }

        if (latestSubscriber is not null)
        {
            snapshot.LastSubscriberName = latestSubscriber.UserName ?? latestSubscriber.UserLogin ?? latestSubscriber.UserId;
            snapshot.LastSubscriberUtc = DateTime.UtcNow;
        }

        snapshot.UpdatedUtc = DateTime.UtcNow;
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

    private static TwitchAuthContext? BuildOverlayAuth(Streamer streamer, TwitchOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken)
            || string.IsNullOrWhiteSpace(streamer.TwitchUserId))
        {
            return null;
        }

        return new TwitchAuthContext(
            clientId,
            streamer.TwitchStreamerAccessToken,
            streamer.TwitchBotUserId,
            streamer.TwitchUserId);
    }
}