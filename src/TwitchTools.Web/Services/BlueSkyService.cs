using TwitchTools.Web.Domain;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class BlueSkyService(
    IBlueSkyApiClient blueSkyApiClient,
    ILogger<BlueSkyService> logger) : IBlueSkyService
{
    private const string DefaultStreamStartedTemplate = "{streamer} is now live on Twitch.";
    private const string DefaultStreamStoppedTemplate = "{streamer} has ended the stream.";

    public async Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, TwitchStreamStatus streamStatus, CancellationToken cancellationToken)
    {
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

        await blueSkyApiClient.UpdateProfileLiveIndicatorAsync(isLive, credentials, cancellationToken);

        logger.LogInformation("BlueSky live state published for {Streamer} -> {IsLive}", streamer.DisplayName, isLive);
        return postUri;
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