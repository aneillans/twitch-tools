using TwitchTools.Web.Domain;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class BlueSkyService(
    IBlueSkyApiClient blueSkyApiClient,
    ITwitchApiClient twitchApiClient,
    IOptions<FeatureFlagsOptions> featureFlags,
    IOptions<TwitchOptions> twitchOptions,
    ILogger<BlueSkyService> logger) : IBlueSkyService
{
    private const string DefaultStreamStartedTemplate = "{streamer} is now live on Twitch.";
    private const string DefaultStreamStoppedTemplate = "{streamer} has ended the stream.";

    public async Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, TwitchStreamStatus streamStatus, CancellationToken cancellationToken)
    {
        if (featureFlags.Value.DisableExternalPosting)
        {
            logger.LogInformation("Skipping BlueSky publish for {Streamer} because FeatureFlags:DisableExternalPosting is enabled.", streamer.DisplayName);
            return null;
        }

        if (string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier) || string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword))
        {
            logger.LogWarning("Skipping BlueSky publish for {Streamer} because credentials are missing.", streamer.DisplayName);
            return null;
        }

        var credentials = new BlueSkyCredentials(streamer.BlueSkyIdentifier, streamer.BlueSkyAppPassword);
        var shouldPublishPost = isLive ? streamer.BlueSkyPostOnStreamStart : streamer.BlueSkyPostOnStreamStop;
        string? postUri = null;
        if (shouldPublishPost)
        {
            var postText = BuildPostText(streamer, isLive, streamStatus);
            postUri = await blueSkyApiClient.PublishLiveStatePostAsync(postText, credentials, cancellationToken);
        }
        else
        {
            logger.LogInformation("Skipping BlueSky post for {Streamer} because posting is disabled for IsLive={IsLive}.", streamer.DisplayName, isLive);
        }

        // Set the live status indicator using the dedicated app.bsky.actor.status record
        var streamUrl = await GetTwitchStreamUrlAsync(streamer, cancellationToken);
        await blueSkyApiClient.SetLiveStatusAsync(isLive, streamUrl, durationMinutes: 240, credentials, cancellationToken);

        // Also update the profile for backwards compatibility
        await blueSkyApiClient.UpdateProfileLiveIndicatorAsync(isLive, credentials, cancellationToken);

        logger.LogInformation("BlueSky live state published for {Streamer} -> {IsLive}", streamer.DisplayName, isLive);
        return postUri;
    }

    private async Task<string?> GetTwitchStreamUrlAsync(Streamer streamer, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(streamer.TwitchUserId) || string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken))
            {
                return null;
            }

            var twitchOpts = twitchOptions.Value;
            if (string.IsNullOrWhiteSpace(twitchOpts.DefaultClientId))
            {
                return null;
            }

            var authContext = new TwitchAuthContext(twitchOpts.DefaultClientId, streamer.TwitchStreamerAccessToken, null, null);
            var users = await twitchApiClient.GetUsersByIdsAsync(new[] { streamer.TwitchUserId }, authContext, cancellationToken);

            if (users.TryGetValue(streamer.TwitchUserId, out var userProfile) && !string.IsNullOrWhiteSpace(userProfile.UserLogin))
            {
                return $"https://twitch.tv/{userProfile.UserLogin}";
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get Twitch stream URL for streamer {StreamerDisplayName}", streamer.DisplayName);
            return null;
        }
    }

    private static string BuildPostText(Streamer streamer, bool isLive, TwitchStreamStatus streamStatus)
    {
        var template = isLive
            ? streamer.BlueSkyStreamStartedTemplate
            : streamer.BlueSkyStreamStoppedTemplate;

        template = string.IsNullOrWhiteSpace(template)
            ? (isLive ? DefaultStreamStartedTemplate : DefaultStreamStoppedTemplate)
            : template.Trim();

        var streamerName = string.IsNullOrWhiteSpace(streamer.DisplayName)
            ? "Streamer"
            : streamer.DisplayName;

        var streamTitle = string.IsNullOrWhiteSpace(streamStatus.StreamTitle) ? "Untitled stream" : streamStatus.StreamTitle;
        var gameName = string.IsNullOrWhiteSpace(streamStatus.GameName) ? "Uncategorized" : streamStatus.GameName;

        var text = template
            .Replace("{streamer}", streamerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", streamTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{game}", gameName, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (text.Length == 0)
        {
            return isLive
                ? DefaultStreamStartedTemplate.Replace("{streamer}", streamerName, StringComparison.Ordinal)
                : DefaultStreamStoppedTemplate.Replace("{streamer}", streamerName, StringComparison.Ordinal);
        }

        return text;
    }
}