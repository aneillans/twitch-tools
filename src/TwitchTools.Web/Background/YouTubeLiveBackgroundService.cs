using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Background;

/// <summary>
/// Polls YouTube for each connected streamer: detects when they go live, then polls their live
/// chat and republishes messages into the same overlay SSE stream Twitch chat uses (tagged
/// "platform": "youtube"), and hands new messages off to <see cref="ICrossPostChatService"/> for
/// optional Twitch forwarding. There is no push/webhook equivalent of Twitch EventSub for YouTube
/// live chat, so this has to poll.
/// </summary>
public sealed class YouTubeLiveBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<YouTubeOptions> youTubeOptions,
    ILogger<YouTubeLiveBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    // Per-streamer next-allowed-poll time, kept in-memory across ticks so we don't call the
    // YouTube Data API (and burn its per-project daily quota) faster than necessary. Lost on
    // restart, which just means the next tick polls immediately - harmless.
    private readonly ConcurrentDictionary<Guid, DateTime> _nextPollUtc = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollDueStreamersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "YouTube live/chat polling cycle failed.");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task PollDueStreamersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var youTubeApiClient = scope.ServiceProvider.GetRequiredService<IYouTubeApiClient>();
        var overlayEventBroker = scope.ServiceProvider.GetRequiredService<IOverlayEventBroker>();
        var crossPostChatService = scope.ServiceProvider.GetRequiredService<ICrossPostChatService>();
        var viewerMonitoringService = scope.ServiceProvider.GetRequiredService<IYouTubeViewerMonitoringService>();
        var options = youTubeOptions.Value;

        var now = DateTime.UtcNow;
        var streamers = await dbContext.Streamers
            .Where(x => x.YouTubeChannelId != null && x.YouTubeStreamerAccessToken != null)
            .ToListAsync(cancellationToken);

        foreach (var streamer in streamers)
        {
            if (_nextPollUtc.TryGetValue(streamer.Id, out var nextPoll) && nextPoll > now)
            {
                continue;
            }

            try
            {
                await PollStreamerAsync(streamer, dbContext, youTubeApiClient, overlayEventBroker, crossPostChatService, viewerMonitoringService, options, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "YouTube polling failed for {Streamer}.", streamer.DisplayName);
                _nextPollUtc[streamer.Id] = now.AddSeconds(options.NotLivePollIntervalSeconds);
            }
        }
    }

    private async Task PollStreamerAsync(
        Streamer streamer,
        AppDbContext dbContext,
        IYouTubeApiClient youTubeApiClient,
        IOverlayEventBroker overlayEventBroker,
        ICrossPostChatService crossPostChatService,
        IYouTubeViewerMonitoringService viewerMonitoringService,
        YouTubeOptions options,
        CancellationToken cancellationToken)
    {
        var auth = new YouTubeAuthContext(streamer.YouTubeStreamerAccessToken!);
        var state = await dbContext.YouTubeLiveStates.FirstOrDefaultAsync(x => x.StreamerId == streamer.Id, cancellationToken);
        if (state is null)
        {
            state = new YouTubeLiveState { StreamerId = streamer.Id };
            dbContext.YouTubeLiveStates.Add(state);
        }

        var now = DateTime.UtcNow;

        if (!state.IsLive || string.IsNullOrWhiteSpace(state.LiveChatId))
        {
            var broadcast = await youTubeApiClient.GetActiveLiveBroadcastAsync(auth, cancellationToken);
            if (broadcast is null)
            {
                state.IsLive = false;
                state.LastPolledUtc = now;
                state.UpdatedUtc = now;
                await dbContext.SaveChangesAsync(cancellationToken);
                _nextPollUtc[streamer.Id] = now.AddSeconds(options.NotLivePollIntervalSeconds);
                return;
            }

            state.IsLive = true;
            state.VideoId = broadcast.VideoId;
            state.LiveChatId = broadcast.LiveChatId;
            state.NextPageToken = null;
            logger.LogInformation(
                "YouTube live broadcast detected for {Streamer}. VideoId={VideoId}",
                streamer.DisplayName,
                broadcast.VideoId);
        }

        var chatResult = await youTubeApiClient.GetLiveChatMessagesAsync(state.LiveChatId!, state.NextPageToken, auth, cancellationToken);
        if (!chatResult.IsSuccess)
        {
            // Most commonly the broadcast has ended (liveChatId stops resolving); fall back to
            // live-detection polling instead of retrying the same dead chat id.
            logger.LogInformation(
                "YouTube live chat polling stopped for {Streamer}; treating as offline. {ErrorMessage}",
                streamer.DisplayName,
                chatResult.ErrorMessage);
            state.IsLive = false;
            state.LiveChatId = null;
            state.NextPageToken = null;
            state.LastPolledUtc = now;
            state.UpdatedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            _nextPollUtc[streamer.Id] = now.AddSeconds(options.NotLivePollIntervalSeconds);
            return;
        }

        foreach (var message in chatResult.Messages)
        {
            await ProcessMessageAsync(streamer, state.VideoId!, message, overlayEventBroker, crossPostChatService, viewerMonitoringService, cancellationToken);
        }

        state.NextPageToken = chatResult.NextPageToken;
        state.LastPolledUtc = now;
        state.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var pollingIntervalSeconds = (chatResult.PollingIntervalMillis ?? options.MinChatPollIntervalSeconds * 1000) / 1000;
        var delaySeconds = Math.Max(options.MinChatPollIntervalSeconds, pollingIntervalSeconds);
        _nextPollUtc[streamer.Id] = now.AddSeconds(delaySeconds);
    }

    private static async Task ProcessMessageAsync(
        Streamer streamer,
        string videoId,
        YouTubeChatMessage message,
        IOverlayEventBroker overlayEventBroker,
        ICrossPostChatService crossPostChatService,
        IYouTubeViewerMonitoringService viewerMonitoringService,
        CancellationToken cancellationToken)
    {
        // Our own cross-post bot echoing a message back onto YouTube is not a new chat message -
        // it's the mirrored copy of one already shown when it arrived on Twitch. Drop it here so it
        // is not published a second time, is not forwarded back to Twitch, and is not counted as
        // viewer watch time (see CrossPostChatService and
        // TwitchEventSubService.HandleChatMessageAsync for the other side of this same rule).
        if (!string.IsNullOrWhiteSpace(streamer.YouTubeBotChannelId)
            && string.Equals(message.AuthorChannelId, streamer.YouTubeBotChannelId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.AuthorChannelId))
        {
            await viewerMonitoringService.RecordViewerActivityAsync(
                streamer,
                videoId,
                message.AuthorChannelId,
                message.AuthorDisplayName,
                message.PublishedAtUtc ?? DateTimeOffset.UtcNow,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(message.AuthorDisplayName) ? "YouTube viewer" : message.AuthorDisplayName;

        var payload = new
        {
            listener = "message",
            platform = "youtube",
            @event = new
            {
                data = new
                {
                    text = message.MessageText,
                    displayName,
                    nick = displayName,
                    userId = message.AuthorChannelId ?? string.Empty,
                    msgId = message.MessageId,
                    badges = Array.Empty<object>(),
                    tags = new
                    {
                        badges = string.Empty,
                        subscriber = "0",
                        mod = "0",
                        color = "#FFFFFF"
                    }
                },
                renderedText = message.MessageText
            }
        };

        await overlayEventBroker.PublishAsync(streamer.CustomOverlayToken, JsonSerializer.Serialize(payload), cancellationToken);

        await crossPostChatService.CrossPostFromYouTubeAsync(streamer, displayName, message.MessageText, cancellationToken);
    }
}
