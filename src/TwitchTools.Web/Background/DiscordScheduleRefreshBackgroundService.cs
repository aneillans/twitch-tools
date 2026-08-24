using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Background;

public sealed class DiscordScheduleRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DiscordScheduleRefreshBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
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
                var discordSyncService = scope.ServiceProvider.GetRequiredService<IDiscordScheduleSyncService>();

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
                    var hasDiscordTargets = discordSyncTargets.TryGetValue(streamer.Id, out var oldestLastSyncedUtc);
                    var shouldRefreshDiscord = hasDiscordTargets && IsDiscordRefreshDue(streamer.Id, oldestLastSyncedUtc, now);
                    if (!shouldRefreshDiscord)
                    {
                        continue;
                    }

                    await TryRefreshDiscordScheduleAsync(discordSyncService, streamer, now, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discord schedule refresh cycle failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task TryRefreshDiscordScheduleAsync(
        IDiscordScheduleSyncService discordSyncService,
        Streamer streamer,
        DateTime now,
        CancellationToken stoppingToken)
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

    private sealed record DiscordRefreshState(DateTime NextAttemptUtc, int ConsecutiveFailures);
}