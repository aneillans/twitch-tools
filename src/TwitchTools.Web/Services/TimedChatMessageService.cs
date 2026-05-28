using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class TimedChatMessageService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<FeatureFlagsOptions> featureFlags,
    ILogger<TimedChatMessageService> logger) : ITimedChatMessageService
{
    public async Task DispatchDueMessagesAsync(CancellationToken cancellationToken)
    {
        if (featureFlags.Value.DisableExternalPosting)
        {
            logger.LogInformation("Skipping timed chat message dispatch because FeatureFlags:DisableExternalPosting is enabled.");
            return;
        }

        var now = DateTime.UtcNow;
        var liveStreamerIds = await dbContext.LiveNotificationEvents
            .Where(x => x.IsLive)
            .Where(x => x.RecordedUtc == dbContext.LiveNotificationEvents
                .Where(y => y.StreamerId == x.StreamerId)
                .Max(y => y.RecordedUtc))
            .Select(x => x.StreamerId)
            .ToListAsync(cancellationToken);

        if (liveStreamerIds.Count == 0)
        {
            logger.LogDebug("Skipping timed chat message dispatch because no streamers are currently live.");
            return;
        }

        var dueMessages = await dbContext.TimedChatMessages
            .Include(x => x.Streamer)
            .Where(x => liveStreamerIds.Contains(x.StreamerId))
            .Where(x => x.Enabled)
            .Where(x => !x.LastSentUtc.HasValue || x.LastSentUtc <= now - x.Interval)
            .ToListAsync(cancellationToken);

        foreach (var message in dueMessages)
        {
            var auth = BuildAuth(message.Streamer, twitchOptions.Value);
            if (auth is null)
            {
                logger.LogWarning("Skipping timed message for {Streamer} because Twitch auth is not configured.", message.Streamer.DisplayName);
                continue;
            }

            await twitchApiClient.SendChatMessageAsync(message.Streamer.TwitchUserId, message.MessageText, auth, cancellationToken);
            message.LastSentUtc = now;
        }

        if (dueMessages.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Dispatched {Count} timed chat messages.", dueMessages.Count);
    }

    private static TwitchAuthContext? BuildAuth(Domain.Streamer streamer, TwitchOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        var tokenToUse = string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken)
            ? streamer.TwitchStreamerAccessToken
            : streamer.TwitchBotAccessToken;
        var actingUserId = !string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken)
            && !string.IsNullOrWhiteSpace(streamer.TwitchBotUserId)
            ? streamer.TwitchBotUserId
            : streamer.TwitchUserId;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tokenToUse) || string.IsNullOrWhiteSpace(actingUserId))
        {
            return null;
        }

        return new TwitchAuthContext(
            clientId,
            tokenToUse,
            actingUserId,
            actingUserId);
    }
}