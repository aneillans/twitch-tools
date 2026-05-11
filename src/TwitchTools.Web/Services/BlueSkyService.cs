using TwitchTools.Web.Domain;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class BlueSkyService(
    IBlueSkyApiClient blueSkyApiClient,
    ILogger<BlueSkyService> logger) : IBlueSkyService
{
    private const string DefaultStreamStartedTemplate = "{streamer} is now live on Twitch.";
    private const string DefaultStreamStoppedTemplate = "{streamer} has ended the stream.";

    public async Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier) || string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword))
        {
            logger.LogWarning("Skipping BlueSky publish for {Streamer} because credentials are missing.", streamer.DisplayName);
            return null;
        }

        var credentials = new BlueSkyCredentials(streamer.BlueSkyIdentifier, streamer.BlueSkyAppPassword);
        var postText = BuildPostText(streamer, isLive);
        var postUri = await blueSkyApiClient.PublishLiveStatePostAsync(postText, credentials, cancellationToken);
        await blueSkyApiClient.UpdateProfileLiveIndicatorAsync(isLive, credentials, cancellationToken);

        logger.LogInformation("BlueSky live state published for {Streamer} -> {IsLive}", streamer.DisplayName, isLive);
        return postUri;
    }

    private static string BuildPostText(Streamer streamer, bool isLive)
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

        var text = template.Replace("{streamer}", streamerName, StringComparison.OrdinalIgnoreCase).Trim();
        if (text.Length == 0)
        {
            return isLive
                ? DefaultStreamStartedTemplate.Replace("{streamer}", streamerName, StringComparison.Ordinal)
                : DefaultStreamStoppedTemplate.Replace("{streamer}", streamerName, StringComparison.Ordinal);
        }

        return text;
    }
}