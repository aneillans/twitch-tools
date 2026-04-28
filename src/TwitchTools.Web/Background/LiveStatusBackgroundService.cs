using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Background;

public sealed class LiveStatusBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<TwitchOptions> twitchOptions,
    ILogger<LiveStatusBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var blueSkyService = scope.ServiceProvider.GetRequiredService<IBlueSkyService>();
                var discordSyncService = scope.ServiceProvider.GetRequiredService<IDiscordScheduleSyncService>();
                var twitchApiClient = scope.ServiceProvider.GetRequiredService<ITwitchApiClient>();

                var streamers = await dbContext.Streamers.AsNoTracking().ToListAsync(stoppingToken);
                foreach (var streamer in streamers)
                {
                    var auth = BuildAuth(streamer, twitchOptions.Value);
                    if (auth is null)
                    {
                        logger.LogWarning("Skipping live check for {Streamer} because Twitch auth is not configured.", streamer.DisplayName);
                        continue;
                    }

                    var isLive = await twitchApiClient.IsStreamerLiveAsync(streamer.TwitchUserId, auth, stoppingToken);

                    var previous = await dbContext.LiveNotificationEvents
                        .AsNoTracking()
                        .Where(x => x.StreamerId == streamer.Id)
                        .OrderByDescending(x => x.RecordedUtc)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (previous?.IsLive == isLive)
                    {
                        continue;
                    }

                    var postUri = await blueSkyService.PublishLiveStateAsync(streamer, isLive, stoppingToken);

                    if (isLive)
                    {
                        await discordSyncService.SyncScheduleAsync(streamer, stoppingToken);
                    }

                    dbContext.LiveNotificationEvents.Add(new LiveNotificationEvent
                    {
                        StreamerId = streamer.Id,
                        IsLive = isLive,
                        BlueSkyPostUri = postUri,
                        RecordedUtc = DateTime.UtcNow
                    });
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live status monitoring cycle failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private static TwitchAuthContext? BuildAuth(Streamer streamer, TwitchOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId) ? options.DefaultClientId : streamer.TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken))
        {
            return null;
        }

        return new TwitchAuthContext(
            clientId,
            streamer.TwitchStreamerAccessToken,
            streamer.TwitchBotUserId ?? options.DefaultBotUserId,
            streamer.TwitchModeratorUserId ?? options.DefaultModeratorUserId);
    }
}