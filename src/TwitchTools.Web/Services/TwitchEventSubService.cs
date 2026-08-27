using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public sealed class TwitchEventSubService(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<FeatureFlagsOptions> featureFlags,
    IBlueSkyService blueSkyService,
    IDiscordScheduleSyncService discordScheduleSyncService,
    IOverlayEventBroker overlayEventBroker,
    IEventSubStreamStatusDispatcher streamStatusDispatcher,
    ILogger<TwitchEventSubService> logger) : ITwitchEventSubService
{
    // How long a Twitch-Eventsub-Message-Id is remembered for duplicate detection. Twitch redelivers
    // notifications for a limited window when it does not receive a timely 2xx response, so this only
    // needs to comfortably outlast that retry window.
    private static readonly TimeSpan ProcessedMessageRetention = TimeSpan.FromDays(3);
    private const string ChannelSubscribeType = "channel.subscribe";
    private const string ChannelSubscriptionMessageType = "channel.subscription.message";
    private const string ChannelChatMessageType = "channel.chat.message";
    private const string StreamOnlineType = "stream.online";
    private const string StreamOfflineType = "stream.offline";

    private static readonly string[] ChannelSubscriptionRequiredScopes = ["channel:read:subscriptions"];
    private static readonly string[] ChatUserRequiredScopes = ["user:read:chat", "user:bot"];
    private static readonly string[] ChatBroadcasterRequiredScopes = ["channel:bot"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record EventSubEnsureSummary(int EnsuredCount, int AlreadyExistsCount, int FailedCount, string? Message);

    public async Task<EventSubWebhookResult> HandleWebhookAsync(
        string messageType,
        string messageId,
        string messageTimestamp,
        string messageSignature,
        string rawBody,
        CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.EventSubSecret))
        {
            logger.LogError("EventSub webhook received but Twitch:EventSubSecret is not configured.");
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!IsValidSignature(options.EventSubSecret, messageId, messageTimestamp, rawBody, messageSignature))
        {
            logger.LogWarning("Rejected EventSub webhook because the signature did not match.");
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status403Forbidden);
        }

        // Twitch redelivers a notification (with the same Twitch-Eventsub-Message-Id) if it does not
        // receive a timely 2xx response. Claim the message id atomically before doing any further work
        // so redeliveries are ignored instead of triggering duplicate side effects (e.g. duplicate
        // BlueSky posts). This is the primary duplicate guard; per-transition checks further down are
        // only a secondary safety net.
        if (!await TryClaimMessageAsync(messageId, cancellationToken))
        {
            logger.LogInformation(
                "Ignoring duplicate EventSub delivery. MessageType={MessageType}, MessageId={MessageId}",
                messageType,
                messageId);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        await SavePayloadForDebugIfEnabledAsync(messageType, messageId, rawBody, cancellationToken);

        if (string.Equals(messageType, "webhook_callback_verification", StringComparison.OrdinalIgnoreCase))
        {
            using var challengeDocument = JsonDocument.Parse(rawBody);
            if (!challengeDocument.RootElement.TryGetProperty("challenge", out var challengeElement) || challengeElement.ValueKind != JsonValueKind.String)
            {
                return new EventSubWebhookResult(StatusCode: StatusCodes.Status400BadRequest);
            }

            var challenge = challengeElement.GetString() ?? string.Empty;
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status200OK, Body: challenge, ContentType: "text/plain");
        }

        if (string.Equals(messageType, "revocation", StringComparison.OrdinalIgnoreCase))
        {
            using var revocationDocument = JsonDocument.Parse(rawBody);
            var revocationSubscriptionType = revocationDocument.RootElement
                .GetProperty("subscription")
                .GetProperty("type")
                .GetString();

            var status = revocationDocument.RootElement
                .GetProperty("subscription")
                .GetProperty("status")
                .GetString();

            logger.LogWarning(
                "EventSub subscription revoked. Type={SubscriptionType}, Status={Status}, MessageId={MessageId}",
                revocationSubscriptionType,
                status,
                messageId);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        if (!string.Equals(messageType, "notification", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ignoring EventSub message type {MessageType}.", messageType);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        using var notificationDocument = JsonDocument.Parse(rawBody);
        var subscription = notificationDocument.RootElement.GetProperty("subscription");
        var subscriptionType = subscription.GetProperty("type").GetString();

        if (string.Equals(subscriptionType, "channel.chat.message", StringComparison.OrdinalIgnoreCase))
        {
            await HandleChatMessageAsync(notificationDocument.RootElement.GetProperty("event"), cancellationToken);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        if (string.Equals(subscriptionType, StreamOnlineType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subscriptionType, StreamOfflineType, StringComparison.OrdinalIgnoreCase))
        {
            var isLive = string.Equals(subscriptionType, StreamOnlineType, StringComparison.OrdinalIgnoreCase);
            var rawEventJson = notificationDocument.RootElement.GetProperty("event").GetRawText();

            // Hand off to a background worker so we can acknowledge Twitch immediately instead of
            // performing slow downstream work (Twitch API calls, Discord sync, BlueSky posting)
            // inline with the webhook request, which previously risked Twitch's delivery timing out
            // and redelivering the notification before we had finished (and before our duplicate
            // guard had persisted anything).
            streamStatusDispatcher.Enqueue(new StreamStatusEventWorkItem(isLive, rawEventJson, messageId));
            logger.LogInformation(
                "Queued {EventType} notification for background processing. MessageId={MessageId}",
                isLive ? StreamOnlineType : StreamOfflineType,
                messageId);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        if (!string.Equals(subscriptionType, "channel.subscribe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subscriptionType, "channel.subscription.message", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ignoring EventSub notification type {SubscriptionType}.", subscriptionType);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        await RecordSubscriberAsync(notificationDocument.RootElement.GetProperty("event"), cancellationToken);
        return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
    }

    public async Task ProcessQueuedStreamStatusEventAsync(StreamStatusEventWorkItem workItem, CancellationToken cancellationToken)
    {
        using var eventDocument = JsonDocument.Parse(workItem.RawEventJson);
        await HandleStreamStatusAsync(eventDocument.RootElement, workItem.IsLive, cancellationToken);
    }

    /// <summary>
    /// Atomically records that a Twitch-Eventsub-Message-Id is being processed. Returns false if the
    /// message id has already been claimed (i.e. this is a redelivery of a notification we have already
    /// accepted), in which case the caller should skip processing entirely.
    /// </summary>
    private async Task<bool> TryClaimMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            // Without a message id we have no way to deduplicate; allow processing rather than
            // silently dropping the notification.
            logger.LogWarning("EventSub webhook received without a Twitch-Eventsub-Message-Id; duplicate detection is not possible for this delivery.");
            return true;
        }

        var cutoffUtc = DateTime.UtcNow - ProcessedMessageRetention;
        await dbContext.ProcessedEventSubMessages
            .Where(x => x.ProcessedUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var rowsInserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "ProcessedEventSubMessages" ("MessageId", "ProcessedUtc") VALUES ({messageId}, {now}) ON CONFLICT ("MessageId") DO NOTHING""",
            cancellationToken);

        return rowsInserted > 0;
    }

    public async Task EnsureSubscriberSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await EnsureSubscriberSubscriptionsCoreAsync(cancellationToken);
    }

    private async Task<EventSubEnsureSummary> EnsureSubscriberSubscriptionsCoreAsync(CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId)
            || string.IsNullOrWhiteSpace(options.OAuthClientSecret)
            || string.IsNullOrWhiteSpace(options.EventSubCallbackUrl)
            || string.IsNullOrWhiteSpace(options.EventSubSecret))
        {
            logger.LogWarning("Skipping EventSub subscription bootstrap because Twitch EventSub is not fully configured. DefaultClientIdConfigured={HasClientId}, OAuthClientSecretConfigured={HasClientSecret}, EventSubCallbackUrlConfigured={HasCallback}, EventSubSecretConfigured={HasSecret}",
                !string.IsNullOrWhiteSpace(options.DefaultClientId),
                !string.IsNullOrWhiteSpace(options.OAuthClientSecret),
                !string.IsNullOrWhiteSpace(options.EventSubCallbackUrl),
                !string.IsNullOrWhiteSpace(options.EventSubSecret));
            return new EventSubEnsureSummary(0, 0, 0, "Twitch EventSub is not fully configured.");
        }

        var callbackUri = new Uri(options.EventSubCallbackUrl, UriKind.Absolute);
        if (!string.Equals(callbackUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Skipping EventSub subscription bootstrap because callback URL is not HTTPS: {CallbackUrl}", options.EventSubCallbackUrl);
            return new EventSubEnsureSummary(0, 0, 0, "EventSub callback URL must use HTTPS.");
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(
            options.DefaultClientId,
            options.OAuthClientSecret,
            cancellationToken);

        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            logger.LogWarning("Unable to obtain an app access token for EventSub subscription bootstrap. {ErrorMessage}", appToken.ErrorMessage);
            return new EventSubEnsureSummary(0, 0, 0, $"Unable to obtain an app access token: {appToken.ErrorMessage}");
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var streamers = await dbContext.Streamers
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.TwitchUserId))
            .ToListAsync(cancellationToken);

        if (streamers.Count == 0)
        {
            logger.LogInformation("Skipping EventSub subscription bootstrap because no streamers have a TwitchUserId configured.");
            return new EventSubEnsureSummary(0, 0, 0, "No streamers have a Twitch user id configured.");
        }

        var ensuredCount = 0;
        var alreadyExistsCount = 0;
        var failedCount = 0;

        var authorizationLookup = await BuildAuthorizationLookupAsync(streamers, auth, cancellationToken);
        if (!authorizationLookup.IsSuccess)
        {
            logger.LogWarning(
                "Unable to preflight EventSub user grants; proceeding without grant checks. {ErrorMessage}",
                authorizationLookup.ErrorMessage);
        }

        foreach (var streamer in streamers)
        {
            var subscribeResult = await EnsureSubscriptionTypeAsync(
                streamer,
                auth,
                options,
                ChannelSubscribeType,
                authorizationLookup,
                cancellationToken);
            ensuredCount += subscribeResult.EnsuredCount;
            alreadyExistsCount += subscribeResult.AlreadyExistsCount;
            failedCount += subscribeResult.FailedCount;

            var resubResult = await EnsureSubscriptionTypeAsync(
                streamer,
                auth,
                options,
                ChannelSubscriptionMessageType,
                authorizationLookup,
                cancellationToken);
            ensuredCount += resubResult.EnsuredCount;
            alreadyExistsCount += resubResult.AlreadyExistsCount;
            failedCount += resubResult.FailedCount;

            var streamOnlineResult = await EnsureSubscriptionTypeAsync(
                streamer,
                auth,
                options,
                StreamOnlineType,
                authorizationLookup,
                cancellationToken);
            ensuredCount += streamOnlineResult.EnsuredCount;
            alreadyExistsCount += streamOnlineResult.AlreadyExistsCount;
            failedCount += streamOnlineResult.FailedCount;

            var streamOfflineResult = await EnsureSubscriptionTypeAsync(
                streamer,
                auth,
                options,
                StreamOfflineType,
                authorizationLookup,
                cancellationToken);
            ensuredCount += streamOfflineResult.EnsuredCount;
            alreadyExistsCount += streamOfflineResult.AlreadyExistsCount;
            failedCount += streamOfflineResult.FailedCount;

            if (!string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
            {
                var chatUserId = string.IsNullOrWhiteSpace(streamer.TwitchBotUserId)
                    ? streamer.TwitchUserId
                    : streamer.TwitchBotUserId;

                if (!string.Equals(chatUserId, streamer.TwitchUserId, StringComparison.Ordinal))
                {
                    logger.LogDebug(
                        "Registering EventSub channel.chat.message for {Streamer} using bot user id {ChatUserId}.",
                        streamer.DisplayName,
                        chatUserId);
                }

                var chatResult = await EnsureSubscriptionTypeAsync(
                    streamer,
                    auth,
                    options,
                    ChannelChatMessageType,
                    authorizationLookup,
                    cancellationToken,
                    extraCondition: ("user_id", chatUserId));

                ensuredCount += chatResult.EnsuredCount;
                alreadyExistsCount += chatResult.AlreadyExistsCount;
                failedCount += chatResult.FailedCount;
            }
            else
            {
                logger.LogDebug(
                    "Skipping EventSub subscription channel.chat.message for {Streamer} because CustomOverlayToken is not configured.",
                    streamer.DisplayName);
            }

            await TryPrefillSubscriberSnapshotAsync(streamer, options, cancellationToken);
        }

        logger.LogInformation(
            "EventSub subscription bootstrap completed. Streamers={StreamerCount}, Ensured={EnsuredCount}, AlreadyExists={AlreadyExistsCount}, Failed={FailedCount}",
            streamers.Count,
            ensuredCount,
            alreadyExistsCount,
            failedCount);

        return new EventSubEnsureSummary(ensuredCount, alreadyExistsCount, failedCount, null);
    }

    private async Task<EventSubEnsureSummary> EnsureSubscriptionTypeAsync(
        Streamer streamer,
        TwitchAuthContext appAuth,
        TwitchOptions options,
        string eventSubType,
        TwitchAuthorizationLookupResult authorizationLookup,
        CancellationToken cancellationToken,
        (string Key, string Value)? extraCondition = null)
    {
        if (authorizationLookup.IsSuccess)
        {
            var preflightWarning = BuildPreflightGrantWarning(
                streamer,
                eventSubType,
                extraCondition,
                authorizationLookup.Authorizations);

            if (!string.IsNullOrWhiteSpace(preflightWarning))
            {
                logger.LogWarning("Skipping EventSub subscription {EventSubType} for {Streamer}. {Warning}", eventSubType, streamer.DisplayName, preflightWarning);
                return new EventSubEnsureSummary(0, 0, 1, preflightWarning);
            }
        }

        var condition = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broadcaster_user_id"] = streamer.TwitchUserId
        };

        if (extraCondition.HasValue)
        {
            condition[extraCondition.Value.Key] = extraCondition.Value.Value;
        }

        var result = await twitchApiClient.CreateEventSubSubscriptionAsync(
            appAuth,
            new TwitchEventSubSubscriptionRequest(
                eventSubType,
                "1",
                condition,
                new TwitchEventSubTransport(
                    "webhook",
                    options.EventSubCallbackUrl,
                    options.EventSubSecret)),
            cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Ensured EventSub subscription {EventSubType} for {Streamer}.", eventSubType, streamer.DisplayName);
            return new EventSubEnsureSummary(1, 0, 0, null);
        }

        if (result.IsAlreadyExists)
        {
            logger.LogDebug("EventSub subscription {EventSubType} already exists for {Streamer}.", eventSubType, streamer.DisplayName);
            return new EventSubEnsureSummary(0, 1, 0, null);
        }

        logger.LogWarning(
            "Failed to ensure EventSub subscription {EventSubType} for {Streamer}. StatusCode={StatusCode}. Error={ErrorMessage}",
            eventSubType,
            streamer.DisplayName,
            result.StatusCode,
            result.ErrorMessage);

        return new EventSubEnsureSummary(0, 0, 1, result.ErrorMessage);
    }

    private async Task<TwitchAuthorizationLookupResult> BuildAuthorizationLookupAsync(
        IReadOnlyCollection<Streamer> streamers,
        TwitchAuthContext appAuth,
        CancellationToken cancellationToken)
    {
        var userIds = streamers
            .SelectMany(x => new[] { x.TwitchUserId, x.TwitchBotUserId })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return await twitchApiClient.GetAuthorizationsByUserIdsAsync(userIds, appAuth, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> BuildGrantWarningsAsync(
        IReadOnlyCollection<Streamer> streamers,
        TwitchAuthContext appAuth,
        CancellationToken cancellationToken)
    {
        if (streamers.Count == 0)
        {
            return [];
        }

        var lookupResult = await BuildAuthorizationLookupAsync(streamers, appAuth, cancellationToken);
        if (!lookupResult.IsSuccess)
        {
            return [
                $"Unable to preflight user grants with Twitch API (/helix/authorization/users): {lookupResult.ErrorMessage}"
            ];
        }

        var warnings = new List<string>();
        foreach (var streamer in streamers)
        {
            var subscribeWarning = BuildPreflightGrantWarning(
                streamer,
                ChannelSubscribeType,
                null,
                lookupResult.Authorizations);
            if (!string.IsNullOrWhiteSpace(subscribeWarning))
            {
                warnings.Add($"{streamer.DisplayName}: {subscribeWarning}");
            }

            var subscriptionMessageWarning = BuildPreflightGrantWarning(
                streamer,
                ChannelSubscriptionMessageType,
                null,
                lookupResult.Authorizations);
            if (!string.IsNullOrWhiteSpace(subscriptionMessageWarning))
            {
                warnings.Add($"{streamer.DisplayName}: {subscriptionMessageWarning}");
            }

            if (string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
            {
                continue;
            }

            var chatUserId = string.IsNullOrWhiteSpace(streamer.TwitchBotUserId)
                ? streamer.TwitchUserId
                : streamer.TwitchBotUserId;

            var chatWarning = BuildPreflightGrantWarning(
                streamer,
                ChannelChatMessageType,
                ("user_id", chatUserId),
                lookupResult.Authorizations);
            if (!string.IsNullOrWhiteSpace(chatWarning))
            {
                warnings.Add($"{streamer.DisplayName}: {chatWarning}");
            }
        }

        return warnings;
    }

    private static string? BuildPreflightGrantWarning(
        Streamer streamer,
        string eventSubType,
        (string Key, string Value)? extraCondition,
        IReadOnlyDictionary<string, TwitchUserAuthorization> authorizations)
    {
        if (eventSubType is StreamOnlineType or StreamOfflineType)
        {
            return null;
        }

        if (eventSubType is ChannelSubscribeType or ChannelSubscriptionMessageType)
        {
            return BuildScopeWarning(
                streamer.TwitchUserId,
                $"{eventSubType} requires broadcaster grants",
                ChannelSubscriptionRequiredScopes,
                authorizations);
        }

        if (eventSubType == ChannelChatMessageType)
        {
            var chatUserId = extraCondition.HasValue && string.Equals(extraCondition.Value.Key, "user_id", StringComparison.Ordinal)
                ? extraCondition.Value.Value
                : streamer.TwitchUserId;

            var chatterWarning = BuildScopeWarning(
                chatUserId,
                "channel.chat.message requires chat sender grants",
                ChatUserRequiredScopes,
                authorizations);
            if (!string.IsNullOrWhiteSpace(chatterWarning))
            {
                return chatterWarning;
            }

            return BuildScopeWarning(
                streamer.TwitchUserId,
                "channel.chat.message requires broadcaster grant",
                ChatBroadcasterRequiredScopes,
                authorizations);
        }

        return null;
    }

    private static string? BuildScopeWarning(
        string? userId,
        string context,
        IReadOnlyCollection<string> requiredScopes,
        IReadOnlyDictionary<string, TwitchUserAuthorization> authorizations)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return $"{context}: user id is missing";
        }

        if (!authorizations.TryGetValue(userId, out var authorization))
        {
            return $"{context}: user {userId} has not granted this app yet";
        }

        var scopes = authorization.Scopes.ToHashSet(StringComparer.Ordinal);
        var missingScopes = requiredScopes
            .Where(scope => !scopes.Contains(scope))
            .ToArray();

        if (missingScopes.Length == 0)
        {
            return null;
        }

        var userName = authorization.UserName ?? authorization.UserLogin ?? userId;
        return $"{context}: user {userName} ({userId}) is missing scope(s) {string.Join(", ", missingScopes)}";
    }

    private async Task TryPrefillSubscriberSnapshotAsync(Streamer streamer, TwitchOptions options, CancellationToken cancellationToken)
    {
        var dbStreamer = await dbContext.Streamers
            .Include(x => x.OverlaySnapshot)
            .FirstOrDefaultAsync(x => x.Id == streamer.Id, cancellationToken);

        if (dbStreamer is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(dbStreamer.OverlaySnapshot?.LastSubscriberName))
        {
            return;
        }

        var hasEvents = await dbContext.SubscriberNotificationEvents
            .AsNoTracking()
            .AnyAsync(x => x.StreamerId == streamer.Id, cancellationToken);
        if (hasEvents)
        {
            return;
        }

        var clientId = string.IsNullOrWhiteSpace(dbStreamer.TwitchClientId)
            ? options.DefaultClientId
            : dbStreamer.TwitchClientId;

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(dbStreamer.TwitchStreamerAccessToken)
            || string.IsNullOrWhiteSpace(dbStreamer.TwitchUserId))
        {
            return;
        }

        var auth = new TwitchAuthContext(
            clientId,
            dbStreamer.TwitchStreamerAccessToken,
            dbStreamer.TwitchBotUserId,
            dbStreamer.TwitchUserId);

        var candidate = await twitchApiClient.GetLatestSubscriberAsync(dbStreamer.TwitchUserId, auth, cancellationToken);
        if (candidate is null)
        {
            return;
        }

        var snapshot = dbStreamer.OverlaySnapshot;
        if (snapshot is null)
        {
            snapshot = new OverlaySnapshot
            {
                StreamerId = dbStreamer.Id
            };
            dbContext.OverlaySnapshots.Add(snapshot);
            dbStreamer.OverlaySnapshot = snapshot;
        }

        var recordedUtc = DateTime.UtcNow;
        dbContext.SubscriberNotificationEvents.Add(new SubscriberNotificationEvent
        {
            StreamerId = dbStreamer.Id,
            TwitchUserId = candidate.UserId,
            TwitchUserLogin = candidate.UserLogin,
            TwitchUserName = candidate.UserName,
            IsGift = false,
            RecordedUtc = recordedUtc
        });

        snapshot.LastSubscriberName = candidate.UserName ?? candidate.UserLogin ?? candidate.UserId;
        snapshot.LastSubscriberUtc = recordedUtc;
        snapshot.UpdatedUtc = recordedUtc;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Prefilled subscriber snapshot for {Streamer} using a best-effort Helix subscriptions lookup: {SubscriberName}",
            dbStreamer.DisplayName,
            snapshot.LastSubscriberName);
    }

    private async Task HandleChatMessageAsync(JsonElement eventElement, CancellationToken cancellationToken)
    {
        var broadcasterId = eventElement.TryGetProperty("broadcaster_user_id", out var bElement) && bElement.ValueKind == JsonValueKind.String
            ? bElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return;
        }

        var streamer = await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TwitchUserId == broadcasterId, cancellationToken);

        if (streamer is null || string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
        {
            return;
        }

        var chatterLogin = eventElement.TryGetProperty("chatter_user_login", out var loginElement) && loginElement.ValueKind == JsonValueKind.String
            ? loginElement.GetString() ?? string.Empty
            : string.Empty;
        var chatterName = eventElement.TryGetProperty("chatter_user_name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString() ?? chatterLogin
            : chatterLogin;
        var chatterId = eventElement.TryGetProperty("chatter_user_id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? string.Empty
            : string.Empty;
        var messageId = eventElement.TryGetProperty("message_id", out var msgIdElement) && msgIdElement.ValueKind == JsonValueKind.String
            ? msgIdElement.GetString() ?? string.Empty
            : string.Empty;
        var color = eventElement.TryGetProperty("color", out var colorElement) && colorElement.ValueKind == JsonValueKind.String
            ? colorElement.GetString() ?? string.Empty
            : string.Empty;

        var messageText = string.Empty;
        if (eventElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.Object
            && messageElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            messageText = textElement.GetString() ?? string.Empty;
        }

        var isSub = false;
        var isMod = false;
        var badgesList = new List<object>();
        if (eventElement.TryGetProperty("badges", out var badgesElement) && badgesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var badge in badgesElement.EnumerateArray())
            {
                var setId = badge.TryGetProperty("set_id", out var setIdEl) && setIdEl.ValueKind == JsonValueKind.String
                    ? setIdEl.GetString() ?? string.Empty
                    : string.Empty;
                var badgeId = badge.TryGetProperty("id", out var badgeIdEl) && badgeIdEl.ValueKind == JsonValueKind.String
                    ? badgeIdEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.Equals(setId, "subscriber", StringComparison.OrdinalIgnoreCase)) isSub = true;
                if (string.Equals(setId, "moderator", StringComparison.OrdinalIgnoreCase)) isMod = true;
                badgesList.Add(new { type = setId, version = badgeId });
            }
        }

        var payload = new
        {
            listener = "message",
            @event = new
            {
                data = new
                {
                    text = messageText,
                    displayName = chatterName,
                    nick = chatterLogin,
                    userId = chatterId,
                    msgId = messageId,
                    badges = badgesList,
                    tags = new
                    {
                        badges = string.Empty,
                        subscriber = isSub ? "1" : "0",
                        mod = isMod ? "1" : "0",
                        color = string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color
                    }
                },
                renderedText = messageText
            }
        };

        await overlayEventBroker.PublishAsync(
            streamer.CustomOverlayToken,
            JsonSerializer.Serialize(payload),
            cancellationToken);

        logger.LogDebug(
            "Published chat message to overlay for {Streamer}. User={ChatterName}, MsgId={MessageId}",
            streamer.DisplayName,
            chatterName,
            messageId);
    }

    private async Task HandleStreamStatusAsync(JsonElement eventElement, bool isLive, CancellationToken cancellationToken)
    {
        var broadcasterUserId = eventElement.TryGetProperty("broadcaster_user_id", out var bElement) && bElement.ValueKind == JsonValueKind.String
            ? bElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            logger.LogWarning("EventSub stream status payload is missing broadcaster_user_id.");
            return;
        }

        var streamer = await dbContext.Streamers
            .FirstOrDefaultAsync(x => x.TwitchUserId == broadcasterUserId, cancellationToken);
        if (streamer is null)
        {
            logger.LogWarning("Received {EventType} notification for unknown streamer {BroadcasterUserId}.", isLive ? StreamOnlineType : StreamOfflineType, broadcasterUserId);
            return;
        }

        var previous = await dbContext.LiveNotificationEvents
            .AsNoTracking()
            .Where(x => x.StreamerId == streamer.Id)
            .OrderByDescending(x => x.RecordedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous?.IsLive == isLive)
        {
            logger.LogDebug("Ignoring duplicate {EventType} transition for {Streamer}.", isLive ? StreamOnlineType : StreamOfflineType, streamer.DisplayName);
            return;
        }

        var now = DateTime.UtcNow;
        var streamStatus = new TwitchStreamStatus(isLive, null, null);
        if (isLive)
        {
            var auth = BuildBroadcasterAuth(streamer, twitchOptions.Value);
            if (auth is not null)
            {
                var currentStatus = await twitchApiClient.GetStreamStatusAsync(streamer.TwitchUserId, auth, cancellationToken);
                streamStatus = new TwitchStreamStatus(true, currentStatus.StreamTitle, currentStatus.GameName);
            }

            var timedMessages = await dbContext.TimedChatMessages
                .Where(x => x.StreamerId == streamer.Id)
                .ToListAsync(cancellationToken);
            foreach (var timedMessage in timedMessages)
            {
                timedMessage.LastSentUtc = now;
            }

            var hasDiscordTargets = await dbContext.DiscordGuildSyncs
                .AsNoTracking()
                .AnyAsync(x => x.StreamerId == streamer.Id, cancellationToken);
            if (hasDiscordTargets)
            {
                await TrySyncDiscordScheduleOnLiveAsync(streamer, cancellationToken);
            }
        }

        var postUri = await blueSkyService.PublishLiveStateAsync(streamer, isLive, streamStatus, cancellationToken);

        dbContext.LiveNotificationEvents.Add(new LiveNotificationEvent
        {
            StreamerId = streamer.Id,
            IsLive = isLive,
            BlueSkyPostUri = postUri,
            RecordedUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recorded live transition via EventSub for {Streamer}. IsLive={IsLive}",
            streamer.DisplayName,
            isLive);
    }

    private async Task TrySyncDiscordScheduleOnLiveAsync(Streamer streamer, CancellationToken cancellationToken)
    {
        try
        {
            await discordScheduleSyncService.SyncScheduleAsync(streamer, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discord schedule sync failed on live transition for {Streamer}.", streamer.DisplayName);
        }
    }

    private static TwitchAuthContext? BuildBroadcasterAuth(Streamer streamer, TwitchOptions options)
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

    private async Task RecordSubscriberAsync(JsonElement eventElement, CancellationToken cancellationToken)
    {
        var streamerId = eventElement.GetProperty("broadcaster_user_id").GetString();
        var userId = eventElement.GetProperty("user_id").GetString();
        if (string.IsNullOrWhiteSpace(streamerId) || string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("EventSub channel.subscribe payload was missing broadcaster or user identifiers.");
            return;
        }

        var streamer = await dbContext.Streamers
            .Include(x => x.OverlaySnapshot)
            .FirstOrDefaultAsync(x => x.TwitchUserId == streamerId, cancellationToken);

        if (streamer is null)
        {
            logger.LogWarning("Received channel.subscribe notification for unknown streamer {StreamerId}.", streamerId);
            return;
        }

        var userLogin = eventElement.TryGetProperty("user_login", out var userLoginElement) && userLoginElement.ValueKind == JsonValueKind.String
            ? userLoginElement.GetString()
            : null;
        var userName = eventElement.TryGetProperty("user_name", out var userNameElement) && userNameElement.ValueKind == JsonValueKind.String
            ? userNameElement.GetString()
            : null;
        var isGift = eventElement.TryGetProperty("is_gift", out var isGiftElement) && isGiftElement.ValueKind == JsonValueKind.True;
        var recordedUtc = DateTime.UtcNow;

        dbContext.SubscriberNotificationEvents.Add(new SubscriberNotificationEvent
        {
            StreamerId = streamer.Id,
            TwitchUserId = userId,
            TwitchUserLogin = userLogin,
            TwitchUserName = userName,
            IsGift = isGift,
            RecordedUtc = recordedUtc
        });

        var snapshot = streamer.OverlaySnapshot;
        if (snapshot is null)
        {
            snapshot = new OverlaySnapshot
            {
                StreamerId = streamer.Id
            };
            dbContext.OverlaySnapshots.Add(snapshot);
            streamer.OverlaySnapshot = snapshot;
        }

        snapshot.LastSubscriberName = userName ?? userLogin ?? userId;
        snapshot.LastSubscriberUtc = recordedUtc;
        snapshot.UpdatedUtc = recordedUtc;

        logger.LogInformation(
            "Recorded subscriber event for {Streamer}. User={UserName} ({UserId}), Gift={IsGift}",
            streamer.DisplayName,
            snapshot.LastSubscriberName,
            userId,
            isGift);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<EventSubDiagnosticsResult> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
        {
            return new EventSubDiagnosticsResult(false, [], 0, 0, [], "Twitch credentials are not configured.");
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(options.DefaultClientId, options.OAuthClientSecret, cancellationToken);
        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            return new EventSubDiagnosticsResult(true, [], 0, 0, [], $"Failed to obtain app access token: {appToken.ErrorMessage}");
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var result = await twitchApiClient.GetEventSubSubscriptionsAsync(auth, cancellationToken);
        if (!result.IsSuccess)
        {
            return new EventSubDiagnosticsResult(true, [], 0, 0, [], $"Failed to list EventSub subscriptions: {result.ErrorMessage}");
        }

        var streamers = await dbContext.Streamers
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.TwitchUserId))
            .ToListAsync(cancellationToken);

        var grantWarnings = await BuildGrantWarningsAsync(streamers, auth, cancellationToken);

        var statuses = result.Subscriptions
            .Select(s => new EventSubSubscriptionStatus(s.Id, s.Status, s.Type, s.Version, s.Condition, s.CreatedAt))
            .ToList();

        return new EventSubDiagnosticsResult(true, statuses, result.TotalCost, result.MaxTotalCost, grantWarnings, null);
    }

    public async Task<EventSubForceResyncResult> ForceResyncSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
        {
            logger.LogWarning("Cannot force resync EventSub subscriptions: credentials not configured.");
            return new EventSubForceResyncResult(0, 0, 0, 0, "Twitch credentials are not configured.");
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(options.DefaultClientId, options.OAuthClientSecret, cancellationToken);
        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            logger.LogWarning("Cannot force resync EventSub subscriptions: failed to get app token. {ErrorMessage}", appToken.ErrorMessage);
            return new EventSubForceResyncResult(0, 0, 0, 0, $"Failed to obtain app access token: {appToken.ErrorMessage}");
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var deleteAllResult = await DeleteAllSubscriptionsCoreAsync(auth, cancellationToken);

        var ensureSummary = await EnsureSubscriberSubscriptionsCoreAsync(cancellationToken);
        var message = ensureSummary.Message;

        logger.LogInformation(
            "Force resync of EventSub subscriptions completed. Deleted={DeletedCount}, Ensured={EnsuredCount}, AlreadyExists={AlreadyExistsCount}, Failed={FailedCount}",
            deleteAllResult.DeletedCount,
            ensureSummary.EnsuredCount,
            ensureSummary.AlreadyExistsCount,
            ensureSummary.FailedCount);

        return new EventSubForceResyncResult(
            deleteAllResult.DeletedCount,
            ensureSummary.EnsuredCount,
            ensureSummary.AlreadyExistsCount,
            ensureSummary.FailedCount,
            message);
    }

    public async Task<EventSubDeleteAllResult> DeleteAllSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
        {
            logger.LogWarning("Cannot delete EventSub subscriptions: credentials not configured.");
            return new EventSubDeleteAllResult(0, 0, "Twitch credentials are not configured.");
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(options.DefaultClientId, options.OAuthClientSecret, cancellationToken);
        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            logger.LogWarning("Cannot delete EventSub subscriptions: failed to get app token. {ErrorMessage}", appToken.ErrorMessage);
            return new EventSubDeleteAllResult(0, 0, $"Failed to obtain app access token: {appToken.ErrorMessage}");
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var result = await DeleteAllSubscriptionsCoreAsync(auth, cancellationToken);

        logger.LogInformation(
            "Delete-all EventSub subscriptions completed. Deleted={DeletedCount}, Failed={FailedCount}",
            result.DeletedCount,
            result.FailedCount);

        return result;
    }

    private async Task<EventSubDeleteAllResult> DeleteAllSubscriptionsCoreAsync(
        TwitchAuthContext auth,
        CancellationToken cancellationToken)
    {
        var deletedCount = 0;
        var failedCount = 0;

        var listResult = await twitchApiClient.GetEventSubSubscriptionsAsync(auth, cancellationToken);
        if (!listResult.IsSuccess)
        {
            logger.LogWarning("Unable to list EventSub subscriptions before delete-all. {ErrorMessage}", listResult.ErrorMessage);
            return new EventSubDeleteAllResult(0, 0, $"Failed to list EventSub subscriptions: {listResult.ErrorMessage}");
        }

        foreach (var sub in listResult.Subscriptions)
        {
            var deleted = await twitchApiClient.DeleteEventSubSubscriptionAsync(sub.Id, auth, cancellationToken);
            if (deleted)
            {
                deletedCount++;
                logger.LogInformation("Deleted EventSub subscription {Id} ({Type}) during delete-all.", sub.Id, sub.Type);
            }
            else
            {
                failedCount++;
                logger.LogWarning("Failed to delete EventSub subscription {Id} ({Type}) during delete-all.", sub.Id, sub.Type);
            }
        }

        return new EventSubDeleteAllResult(deletedCount, failedCount, null);
    }

    private static bool IsValidSignature(string secret, string messageId, string messageTimestamp, string rawBody, string messageSignature)
    {
        if (string.IsNullOrWhiteSpace(messageId)
            || string.IsNullOrWhiteSpace(messageTimestamp)
            || string.IsNullOrWhiteSpace(messageSignature))
        {
            return false;
        }

        var message = string.Concat(messageId, messageTimestamp, rawBody);
        var hmac = ComputeHmac(secret, message).ToUpperInvariant();
        var providedSignature = messageSignature.Trim().ToUpperInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hmac),
            Encoding.UTF8.GetBytes(providedSignature));
    }

    private static string ComputeHmac(string secret, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return "sha256=" + Convert.ToHexString(hash).ToUpperInvariant();
    }

    private async Task SavePayloadForDebugIfEnabledAsync(
        string messageType,
        string messageId,
        string rawBody,
        CancellationToken cancellationToken)
    {
        if (!featureFlags.Value.EnableEventSubPayloadLogging)
        {
            return;
        }

        string? subscriptionType = null;
        string? broadcasterUserId = null;
        Guid? streamerId = null;
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (document.RootElement.TryGetProperty("subscription", out var subscription)
                && subscription.ValueKind == JsonValueKind.Object
                && subscription.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String)
            {
                subscriptionType = typeElement.GetString();
            }

            if (document.RootElement.TryGetProperty("event", out var eventElement)
                && eventElement.ValueKind == JsonValueKind.Object
                && eventElement.TryGetProperty("broadcaster_user_id", out var broadcasterElement)
                && broadcasterElement.ValueKind == JsonValueKind.String)
            {
                broadcasterUserId = broadcasterElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Best-effort debug logging only.
        }

        if (!string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            streamerId = await dbContext.Streamers
                .AsNoTracking()
                .Where(x => x.TwitchUserId == broadcasterUserId)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var retentionDays = featureFlags.Value.EventSubPayloadRetentionDays;
        if (retentionDays > 0)
        {
            var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
            var deletedRows = await dbContext.EventSubDebugMessages
                .Where(x => x.RecordedUtc < cutoffUtc)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows > 0)
            {
                logger.LogInformation(
                    "Pruned {Count} EventSub debug payload rows older than {RetentionDays} day(s).",
                    deletedRows,
                    retentionDays);
            }
        }

        dbContext.EventSubDebugMessages.Add(new EventSubDebugMessage
        {
            MessageType = messageType,
            SubscriptionType = subscriptionType,
            MessageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
            BroadcasterUserId = broadcasterUserId,
            StreamerId = streamerId,
            Payload = rawBody,
            RecordedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "EventSub payload captured for debug. MessageType={MessageType}, SubscriptionType={SubscriptionType}, MessageId={MessageId}, StreamerId={StreamerId}",
            messageType,
            subscriptionType ?? "<none>",
            messageId,
            streamerId);
    }
}
