using Exceptionless;
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
    private static readonly TimeSpan MinDiscordRefreshInterval = TimeSpan.FromHours(3);
    private static readonly TimeSpan MaxDiscordRefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxDiscordBackoffInterval = TimeSpan.FromHours(6);
    private readonly Dictionary<Guid, DiscordRefreshState> discordRefreshStates = [];

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
                var discordSyncTargets = await dbContext.DiscordGuildSyncs
                    .AsNoTracking()
                    .GroupBy(x => x.StreamerId)
                    .Select(x => new
                    {
                        StreamerId = x.Key,
                        OldestLastSyncedUtc = x.Min(y => y.LastSyncedUtc)
                    })
                    .ToDictionaryAsync(x => x.StreamerId, x => x.OldestLastSyncedUtc, stoppingToken);

                var now = DateTime.UtcNow;
                PruneDiscordRefreshStates(streamers.Select(x => x.Id));

                foreach (var streamer in streamers)
                {
                    var auth = BuildAuth(streamer, twitchOptions.Value);
                    if (auth is null)
                    {
                        logger.LogWarning("Skipping live check for {Streamer} because Twitch auth is not configured.", streamer.DisplayName);
                        continue;
                    }

                    var streamStatus = await twitchApiClient.GetStreamStatusAsync(streamer.TwitchUserId, auth, stoppingToken);
                    var isLive = streamStatus.IsLive;

                    var previous = await dbContext.LiveNotificationEvents
                        .AsNoTracking()
                        .Where(x => x.StreamerId == streamer.Id)
                        .OrderByDescending(x => x.RecordedUtc)
                        .FirstOrDefaultAsync(stoppingToken);

                    var liveTransition = previous?.IsLive != isLive;
                    var hasDiscordTargets = discordSyncTargets.TryGetValue(streamer.Id, out var oldestLastSyncedUtc);
                    var shouldRefreshDiscord = hasDiscordTargets
                        && (IsDiscordRefreshDue(streamer.Id, oldestLastSyncedUtc, now) || (liveTransition && isLive));
                    if (shouldRefreshDiscord)
                    {
                        try
                        {
                            await discordSyncService.SyncScheduleAsync(streamer, stoppingToken);
                            RegisterDiscordRefreshSuccess(streamer.Id, now);
                        }
                        catch (Exception ex)
                        {
                            RegisterDiscordRefreshFailure(streamer.Id, now);
                            logger.LogError(ex, "Discord schedule sync failed for {Streamer}.", streamer.DisplayName);
                            ExceptionlessClient.Default.SubmitException(ex);
                        }
                    }

                    if (!liveTransition)
                    {
                        continue;
                    }

                    var postUri = await blueSkyService.PublishLiveStateAsync(streamer, isLive, streamStatus, stoppingToken);

                    dbContext.LiveNotificationEvents.Add(new LiveNotificationEvent
                    {
                        StreamerId = streamer.Id,
                        IsLive = isLive,
                        BlueSkyPostUri = postUri,
                        RecordedUtc = now
                    });
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live status monitoring cycle failed.");
                ExceptionlessClient.Default.SubmitException(ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private void PruneDiscordRefreshStates(IEnumerable<Guid> activeStreamerIds)
    {
        var activeIds = activeStreamerIds.ToHashSet();
        foreach (var streamerId in discordRefreshStates.Keys.Where(x => !activeIds.Contains(x)).ToArray())
        {
            discordRefreshStates.Remove(streamerId);
        }
    }

    private bool IsDiscordRefreshDue(Guid streamerId, DateTime oldestLastSyncedUtc, DateTime now)
    {
        if (discordRefreshStates.TryGetValue(streamerId, out var state))
        {
            return now >= state.NextAttemptUtc;
        }

        return oldestLastSyncedUtc == DateTime.UnixEpoch || now - oldestLastSyncedUtc >= MinDiscordRefreshInterval;
    }

    private void RegisterDiscordRefreshSuccess(Guid streamerId, DateTime now)
    {
        discordRefreshStates[streamerId] = new DiscordRefreshState(
            now + NextDiscordRefreshInterval(),
            0);
    }

    private void RegisterDiscordRefreshFailure(Guid streamerId, DateTime now)
    {
        var failureCount = discordRefreshStates.TryGetValue(streamerId, out var state)
            ? state.ConsecutiveFailures + 1
            : 1;

        discordRefreshStates[streamerId] = new DiscordRefreshState(
            now + NextDiscordBackoffInterval(failureCount),
            failureCount);
    }

    private static TimeSpan NextDiscordRefreshInterval()
    {
        var minHours = (int)MinDiscordRefreshInterval.TotalHours;
        var maxHours = (int)MaxDiscordRefreshInterval.TotalHours;
        return TimeSpan.FromHours(Random.Shared.Next(minHours, maxHours + 1));
    }

    private static TimeSpan NextDiscordBackoffInterval(int failureCount)
    {
        var multiplier = Math.Min(1 << Math.Min(failureCount - 1, 4), 16);
        var interval = TimeSpan.FromMinutes(15 * multiplier);
        return interval > MaxDiscordBackoffInterval ? MaxDiscordBackoffInterval : interval;
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
            streamer.TwitchBotUserId ?? options.DefaultBotUserId);
    }

    private sealed record DiscordRefreshState(DateTime NextAttemptUtc, int ConsecutiveFailures);
}