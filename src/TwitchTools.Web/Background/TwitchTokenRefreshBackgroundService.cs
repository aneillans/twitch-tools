using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Background;

public sealed class TwitchTokenRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<TwitchOptions> twitchOptions,
    ILogger<TwitchTokenRefreshBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly int RefreshThresholdSeconds = 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var twitchApiClient = scope.ServiceProvider.GetRequiredService<ITwitchApiClient>();
                var options = twitchOptions.Value;

                var streamers = await dbContext.Streamers.ToListAsync(stoppingToken);
                var hasChanges = false;

                foreach (var streamer in streamers)
                {
                    hasChanges |= await RefreshTokenIfNeededAsync(
                        streamer,
                        isBotToken: false,
                        twitchApiClient,
                        options,
                        stoppingToken);

                    hasChanges |= await RefreshTokenIfNeededAsync(
                        streamer,
                        isBotToken: true,
                        twitchApiClient,
                        options,
                        stoppingToken);
                }

                if (hasChanges)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Twitch token refresh cycle failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task<bool> RefreshTokenIfNeededAsync(
        TwitchTools.Web.Domain.Streamer streamer,
        bool isBotToken,
        ITwitchApiClient twitchApiClient,
        TwitchOptions options,
        CancellationToken cancellationToken)
    {
        var accessToken = isBotToken ? streamer.TwitchBotAccessToken : streamer.TwitchStreamerAccessToken;
        var refreshToken = isBotToken ? streamer.TwitchBotRefreshToken : streamer.TwitchStreamerRefreshToken;
        var tokenLabel = isBotToken ? "bot" : "streamer";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var validation = await twitchApiClient.ValidateAccessTokenAsync(accessToken, cancellationToken);
        if (validation.IsValid
            && (!validation.ExpiresInSeconds.HasValue || validation.ExpiresInSeconds.Value > RefreshThresholdSeconds))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogWarning(
                "Twitch {TokenLabel} token for {Streamer} is {State}, but no refresh token is available.",
                tokenLabel,
                streamer.DisplayName,
                validation.IsValid ? "expiring soon" : "invalid");
            return false;
        }

        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
        {
            logger.LogWarning("Skipping Twitch {TokenLabel} token refresh for {Streamer} because OAuth client settings are incomplete.", tokenLabel, streamer.DisplayName);
            return false;
        }

        var refreshResult = await twitchApiClient.RefreshAccessTokenAsync(refreshToken, clientId, options.OAuthClientSecret, cancellationToken);
        if (!refreshResult.IsSuccess || string.IsNullOrWhiteSpace(refreshResult.AccessToken))
        {
            logger.LogWarning(
                "Failed refreshing Twitch {TokenLabel} token for {Streamer}. Error: {ErrorMessage}",
                tokenLabel,
                streamer.DisplayName,
                refreshResult.ErrorMessage);
            return false;
        }

        if (isBotToken)
        {
            streamer.TwitchBotAccessToken = refreshResult.AccessToken;
            streamer.TwitchBotRefreshToken = string.IsNullOrWhiteSpace(refreshResult.RefreshToken) ? refreshToken : refreshResult.RefreshToken;
        }
        else
        {
            streamer.TwitchStreamerAccessToken = refreshResult.AccessToken;
            streamer.TwitchStreamerRefreshToken = string.IsNullOrWhiteSpace(refreshResult.RefreshToken) ? refreshToken : refreshResult.RefreshToken;
        }

        logger.LogInformation("Refreshed Twitch {TokenLabel} token for {Streamer}.", tokenLabel, streamer.DisplayName);
        return true;
    }
}
