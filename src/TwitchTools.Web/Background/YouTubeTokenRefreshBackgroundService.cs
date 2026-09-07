using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Background;

public sealed class YouTubeTokenRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<YouTubeOptions> youTubeOptions,
    ILogger<YouTubeTokenRefreshBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int RefreshThresholdSeconds = 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var youTubeApiClient = scope.ServiceProvider.GetRequiredService<IYouTubeApiClient>();
                var options = youTubeOptions.Value;

                var streamers = await dbContext.Streamers
                    .Where(x => x.YouTubeStreamerRefreshToken != null || x.YouTubeBotRefreshToken != null)
                    .ToListAsync(stoppingToken);

                var hasChanges = false;

                foreach (var streamer in streamers)
                {
                    hasChanges |= await RefreshTokenIfNeededAsync(streamer, isBotToken: false, youTubeApiClient, options, stoppingToken);
                    hasChanges |= await RefreshTokenIfNeededAsync(streamer, isBotToken: true, youTubeApiClient, options, stoppingToken);
                }

                if (hasChanges)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "YouTube token refresh cycle failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task<bool> RefreshTokenIfNeededAsync(
        Streamer streamer,
        bool isBotToken,
        IYouTubeApiClient youTubeApiClient,
        YouTubeOptions options,
        CancellationToken cancellationToken)
    {
        var accessToken = isBotToken ? streamer.YouTubeBotAccessToken : streamer.YouTubeStreamerAccessToken;
        var refreshToken = isBotToken ? streamer.YouTubeBotRefreshToken : streamer.YouTubeStreamerRefreshToken;
        var tokenLabel = isBotToken ? "bot" : "streamer";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var validation = await youTubeApiClient.ValidateAccessTokenAsync(accessToken, cancellationToken);
        if (validation.IsValid
            && (!validation.ExpiresInSeconds.HasValue || validation.ExpiresInSeconds.Value > RefreshThresholdSeconds))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogWarning(
                "YouTube {TokenLabel} token for {Streamer} is {State}, but no refresh token is available.",
                tokenLabel,
                streamer.DisplayName,
                validation.IsValid ? "expiring soon" : "invalid");
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.DefaultClientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
        {
            logger.LogWarning("Skipping YouTube {TokenLabel} token refresh for {Streamer} because OAuth client settings are incomplete.", tokenLabel, streamer.DisplayName);
            return false;
        }

        var refreshResult = await youTubeApiClient.RefreshAccessTokenAsync(refreshToken, options.DefaultClientId, options.OAuthClientSecret, cancellationToken);
        if (!refreshResult.IsSuccess || string.IsNullOrWhiteSpace(refreshResult.AccessToken))
        {
            logger.LogWarning(
                "Failed refreshing YouTube {TokenLabel} token for {Streamer}. Error: {ErrorMessage}",
                tokenLabel,
                streamer.DisplayName,
                refreshResult.ErrorMessage);
            return false;
        }

        if (isBotToken)
        {
            streamer.YouTubeBotAccessToken = refreshResult.AccessToken;
            streamer.YouTubeBotRefreshToken = string.IsNullOrWhiteSpace(refreshResult.RefreshToken) ? refreshToken : refreshResult.RefreshToken;
        }
        else
        {
            streamer.YouTubeStreamerAccessToken = refreshResult.AccessToken;
            streamer.YouTubeStreamerRefreshToken = string.IsNullOrWhiteSpace(refreshResult.RefreshToken) ? refreshToken : refreshResult.RefreshToken;
        }

        logger.LogInformation("Refreshed YouTube {TokenLabel} token for {Streamer}.", tokenLabel, streamer.DisplayName);
        return true;
    }
}
