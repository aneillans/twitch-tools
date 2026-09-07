using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class CrossPostChatService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IYouTubeApiClient youTubeApiClient,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<FeatureFlagsOptions> featureFlags,
    ILogger<CrossPostChatService> logger) : ICrossPostChatService
{
    public const string DefaultCrossPostTemplate = "[{platform}] {user}: {message}";

    public async Task CrossPostFromTwitchAsync(Streamer streamer, string authorDisplayName, string messageText, CancellationToken cancellationToken)
    {
        if (!CanCrossPost(streamer))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(streamer.YouTubeBotChannelId) || string.IsNullOrWhiteSpace(streamer.YouTubeBotAccessToken))
        {
            return;
        }

        var liveChatId = await dbContext.YouTubeLiveStates
            .AsNoTracking()
            .Where(x => x.StreamerId == streamer.Id && x.IsLive)
            .Select(x => x.LiveChatId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(liveChatId))
        {
            logger.LogDebug("Skipping Twitch->YouTube cross-post for {Streamer} because YouTube is not currently live.", streamer.DisplayName);
            return;
        }

        var formatted = FormatMessage(streamer.CrossPostToYouTubeTemplate, "Twitch", authorDisplayName, messageText);
        var sent = await youTubeApiClient.SendLiveChatMessageAsync(
            liveChatId,
            formatted,
            new YouTubeAuthContext(streamer.YouTubeBotAccessToken),
            cancellationToken);

        if (sent)
        {
            logger.LogDebug("Cross-posted Twitch chat message to YouTube for {Streamer}.", streamer.DisplayName);
        }
    }

    public async Task CrossPostFromYouTubeAsync(Streamer streamer, string authorDisplayName, string messageText, CancellationToken cancellationToken)
    {
        if (!CanCrossPost(streamer))
        {
            return;
        }

        var auth = BuildTwitchBotAuth(streamer, twitchOptions.Value);
        if (auth is null)
        {
            return;
        }

        var formatted = FormatMessage(streamer.CrossPostToTwitchTemplate, "YouTube", authorDisplayName, messageText);
        await twitchApiClient.SendChatMessageAsync(streamer.TwitchUserId, formatted, auth, cancellationToken);

        logger.LogDebug("Cross-posted YouTube chat message to Twitch for {Streamer}.", streamer.DisplayName);
    }

    private bool CanCrossPost(Streamer streamer)
    {
        if (featureFlags.Value.DisableExternalPosting)
        {
            return false;
        }

        return streamer.CrossPostChatEnabled;
    }

    private static string FormatMessage(string? template, string platform, string user, string message)
    {
        var effective = string.IsNullOrWhiteSpace(template) ? DefaultCrossPostTemplate : template;
        return effective
            .Replace("{platform}", platform, StringComparison.Ordinal)
            .Replace("{user}", user, StringComparison.Ordinal)
            .Replace("{message}", message, StringComparison.Ordinal);
    }

    private static TwitchAuthContext? BuildTwitchBotAuth(Streamer streamer, TwitchOptions options)
    {
        if (string.IsNullOrWhiteSpace(streamer.TwitchBotUserId) || string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken))
        {
            return null;
        }

        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(streamer.TwitchUserId))
        {
            return null;
        }

        return new TwitchAuthContext(clientId, streamer.TwitchBotAccessToken, streamer.TwitchBotUserId, streamer.TwitchBotUserId);
    }
}
