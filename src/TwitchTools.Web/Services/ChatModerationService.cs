using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

/// <summary>
/// Applies a mod action from the portal chat window on the platform the message came from, acting
/// as the streamer (broadcaster) account on that platform.
/// </summary>
public sealed class ChatModerationService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IYouTubeApiClient youTubeApiClient,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<FeatureFlagsOptions> featureFlags,
    ILogger<ChatModerationService> logger) : IChatModerationService
{
    public const string TwitchPlatform = "twitch";
    public const string YouTubePlatform = "youtube";

    // Twitch accepts timeouts of 1 second to 2 weeks.
    private const int MaxTimeoutSeconds = 1_209_600;

    public async Task<ChatModerationResult> ModerateAsync(Streamer streamer, ChatModerationRequest request, CancellationToken cancellationToken)
    {
        if (featureFlags.Value.DisableExternalPosting)
        {
            return new ChatModerationResult(false, "Mod actions are disabled because external posting is turned off.");
        }

        if (request.Action == ChatModerationAction.Delete && string.IsNullOrWhiteSpace(request.MessageId))
        {
            return new ChatModerationResult(false, "A message id is required to delete a message.");
        }

        if (request.Action != ChatModerationAction.Delete && string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ChatModerationResult(false, "A user id is required to time out or ban.");
        }

        int? duration = null;
        if (request.Action == ChatModerationAction.Timeout)
        {
            if (request.DurationSeconds is not > 0)
            {
                return new ChatModerationResult(false, "A timeout needs a duration in seconds.");
            }

            duration = Math.Min(request.DurationSeconds.Value, MaxTimeoutSeconds);
        }

        var result = request.Platform switch
        {
            TwitchPlatform => await ModerateTwitchAsync(streamer, request, duration, cancellationToken),
            YouTubePlatform => await ModerateYouTubeAsync(streamer, request, duration, cancellationToken),
            _ => new ChatModerationResult(false, "Unknown chat platform.")
        };

        logger.LogInformation(
            "Chat mod action {Action} on {Platform} for {Streamer}: Success={IsSuccess}, MessageId={MessageId}, UserId={UserId}",
            request.Action,
            request.Platform,
            streamer.DisplayName,
            result.IsSuccess,
            request.MessageId,
            request.UserId);

        return result;
    }

    private async Task<ChatModerationResult> ModerateTwitchAsync(
        Streamer streamer,
        ChatModerationRequest request,
        int? duration,
        CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(streamer.TwitchUserId)
            || string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken))
        {
            return new ChatModerationResult(false, "Connect your Twitch streamer account first.");
        }

        // The broadcaster moderates their own channel, so moderator_id is the broadcaster.
        var auth = new TwitchAuthContext(clientId, streamer.TwitchStreamerAccessToken, null, streamer.TwitchUserId);
        var twitchResult = request.Action == ChatModerationAction.Delete
            ? await twitchApiClient.DeleteChatMessageAsync(streamer.TwitchUserId, streamer.TwitchUserId, request.MessageId!, auth, cancellationToken)
            : await twitchApiClient.BanUserAsync(streamer.TwitchUserId, streamer.TwitchUserId, request.UserId!, duration, auth, cancellationToken);

        if (twitchResult.IsSuccess)
        {
            return new ChatModerationResult(true, SuccessMessage(request.Action, duration));
        }

        return twitchResult.StatusCode is 401 or 403
            ? new ChatModerationResult(false, "Twitch refused the action. Reconnect your Twitch streamer account to grant moderator:manage:chat_messages and moderator:manage:banned_users.")
            : new ChatModerationResult(false, $"Twitch rejected the action (HTTP {twitchResult.StatusCode}).");
    }

    private async Task<ChatModerationResult> ModerateYouTubeAsync(
        Streamer streamer,
        ChatModerationRequest request,
        int? duration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(streamer.YouTubeStreamerAccessToken))
        {
            return new ChatModerationResult(false, "Connect your YouTube channel first.");
        }

        var auth = new YouTubeAuthContext(streamer.YouTubeStreamerAccessToken);
        bool succeeded;
        if (request.Action == ChatModerationAction.Delete)
        {
            succeeded = await youTubeApiClient.DeleteLiveChatMessageAsync(request.MessageId!, auth, cancellationToken);
        }
        else
        {
            var liveChatId = await dbContext.YouTubeLiveStates
                .AsNoTracking()
                .Where(x => x.StreamerId == streamer.Id && x.IsLive)
                .Select(x => x.LiveChatId)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(liveChatId))
            {
                return new ChatModerationResult(false, "YouTube is not live, so there is no live chat to ban from.");
            }

            succeeded = await youTubeApiClient.BanLiveChatUserAsync(liveChatId, request.UserId!, duration, auth, cancellationToken);
        }

        return succeeded
            ? new ChatModerationResult(true, SuccessMessage(request.Action, duration))
            : new ChatModerationResult(false, "YouTube rejected the action. Check the server log for details.");
    }

    private static string SuccessMessage(ChatModerationAction action, int? duration) => action switch
    {
        ChatModerationAction.Delete => "Message deleted.",
        ChatModerationAction.Timeout => $"User timed out for {duration} seconds.",
        _ => "User banned."
    };
}
