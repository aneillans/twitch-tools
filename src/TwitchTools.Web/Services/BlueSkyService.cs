using TwitchTools.Web.Domain;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class BlueSkyService(
    IBlueSkyApiClient blueSkyApiClient,
    ILogger<BlueSkyService> logger) : IBlueSkyService
{
    public async Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier) || string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword))
        {
            logger.LogWarning("Skipping BlueSky publish for {Streamer} because credentials are missing.", streamer.DisplayName);
            return null;
        }

        var credentials = new BlueSkyCredentials(streamer.BlueSkyIdentifier, streamer.BlueSkyAppPassword);
        var postUri = await blueSkyApiClient.PublishLiveStatePostAsync(streamer.DisplayName, isLive, credentials, cancellationToken);
        await blueSkyApiClient.UpdateProfileLiveIndicatorAsync(isLive, credentials, cancellationToken);

        logger.LogInformation("BlueSky live state published for {Streamer} -> {IsLive}", streamer.DisplayName, isLive);
        return postUri;
    }
}