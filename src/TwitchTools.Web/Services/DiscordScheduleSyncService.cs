using TwitchTools.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class DiscordScheduleSyncService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IDiscordApiClient discordApiClient,
    IOptions<TwitchOptions> twitchOptions,
    ILogger<DiscordScheduleSyncService> logger) : IDiscordScheduleSyncService
{
    // Embedded in every event description so the bot only touches events it created.
    private const string BotSignature = "[twitch-tools-sync]";

    public async Task SyncScheduleAsync(Streamer streamer, CancellationToken cancellationToken)
    {
        var auth = BuildAuth(streamer, twitchOptions.Value);
        if (auth is null)
        {
            logger.LogWarning("Skipping Discord schedule sync for {Streamer} because Twitch auth is not configured.", streamer.DisplayName);
            return;
        }

        var schedule = await twitchApiClient.GetScheduleAsync(streamer.TwitchUserId, auth, cancellationToken);

        var syncTargets = await dbContext.DiscordGuildSyncs
            .Where(x => x.StreamerId == streamer.Id)
            .ToListAsync(cancellationToken);

        if (syncTargets.Count == 0)
        {
            return;
        }

        foreach (var target in syncTargets)
        {
            await SyncGuildAsync(target.GuildId, streamer, schedule, cancellationToken);
            target.LastSyncedUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Discord schedule sync complete for {Streamer}", streamer.DisplayName);
    }

    private async Task SyncGuildAsync(
        string guildId,
        Streamer streamer,
        IReadOnlyCollection<TwitchScheduleSegment> schedule,
        CancellationToken cancellationToken)
    {
        var existingEvents = await discordApiClient.GetGuildEventsAsync(guildId, cancellationToken);

        // Only manage events the bot created — identified by the signature in the description.
        var botEvents = existingEvents
            .Where(e => e.Description?.Contains(BotSignature) == true)
            .ToDictionary(e => ExtractSegmentId(e.Description), e => e);

        var incomingById = schedule.ToDictionary(s => s.SegmentId);

        // Create or update.
        foreach (var segment in schedule)
        {
            var payload = BuildPayload(segment, streamer);

            if (botEvents.TryGetValue(segment.SegmentId, out var existing))
            {
                await discordApiClient.UpdateGuildEventAsync(guildId, existing.Id, payload, cancellationToken);
                logger.LogDebug("Updated Discord event {EventId} for segment {SegmentId} in guild {GuildId}",
                    existing.Id, segment.SegmentId, guildId);
            }
            else
            {
                await discordApiClient.CreateGuildEventAsync(guildId, payload, cancellationToken);
                logger.LogDebug("Created Discord event for segment {SegmentId} in guild {GuildId}",
                    segment.SegmentId, guildId);
            }
        }

        // Delete stale bot-owned events whose Twitch segment no longer exists.
        foreach (var (segmentId, discordEvent) in botEvents)
        {
            if (!string.IsNullOrWhiteSpace(segmentId) && !incomingById.ContainsKey(segmentId))
            {
                await discordApiClient.DeleteGuildEventAsync(guildId, discordEvent.Id, cancellationToken);
                logger.LogDebug("Deleted stale Discord event {EventId} for segment {SegmentId} in guild {GuildId}",
                    discordEvent.Id, segmentId, guildId);
            }
        }
    }

    private static DiscordGuildEventPayload BuildPayload(TwitchScheduleSegment segment, Streamer streamer)
    {
        var categoryLine = string.IsNullOrWhiteSpace(segment.CategoryName)
            ? string.Empty
            : $"\nCategory: {segment.CategoryName}";

        var description =
            $"{streamer.DisplayName} is live on Twitch{categoryLine}\n" +
            $"https://twitch.tv/{segment.BroadcasterLogin}\n" +
            $"{BotSignature}:{segment.SegmentId}";

        var name = string.IsNullOrWhiteSpace(segment.Title)
            ? $"{streamer.DisplayName} stream"
            : segment.Title;

        return new DiscordGuildEventPayload(
            name,
            description,
            segment.StartTimeUtc,
            segment.EndTimeUtc,
            $"https://twitch.tv/{segment.BroadcasterLogin}");
    }

    private static string ExtractSegmentId(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var marker = BotSignature + ":";
        var idx = description.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        var start = idx + marker.Length;
        var end = description.IndexOf('\n', start);
        return end < 0
            ? description[start..].Trim()
            : description[start..end].Trim();
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