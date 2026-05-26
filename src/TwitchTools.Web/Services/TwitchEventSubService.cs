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
    IOverlayEventBroker overlayEventBroker,
    ILogger<TwitchEventSubService> logger) : ITwitchEventSubService
{
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

        if (!string.Equals(subscriptionType, "channel.subscribe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subscriptionType, "channel.subscription.message", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ignoring EventSub notification type {SubscriptionType}.", subscriptionType);
            return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
        }

        await RecordSubscriberAsync(notificationDocument.RootElement.GetProperty("event"), cancellationToken);
        return new EventSubWebhookResult(StatusCode: StatusCodes.Status204NoContent);
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

        foreach (var streamer in streamers)
        {
            var subscribeResult = await EnsureSubscriptionTypeAsync(streamer, auth, options, "channel.subscribe", cancellationToken);
            ensuredCount += subscribeResult.EnsuredCount;
            alreadyExistsCount += subscribeResult.AlreadyExistsCount;
            failedCount += subscribeResult.FailedCount;

            var resubResult = await EnsureSubscriptionTypeAsync(streamer, auth, options, "channel.subscription.message", cancellationToken);
            ensuredCount += resubResult.EnsuredCount;
            alreadyExistsCount += resubResult.AlreadyExistsCount;
            failedCount += resubResult.FailedCount;

            if (!string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
            {
                var chatResult = await EnsureSubscriptionTypeAsync(
                    streamer,
                    auth,
                    options,
                    "channel.chat.message",
                    cancellationToken,
                    extraCondition: ("user_id", streamer.TwitchUserId));

                ensuredCount += chatResult.EnsuredCount;
                alreadyExistsCount += chatResult.AlreadyExistsCount;
                failedCount += chatResult.FailedCount;
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
        CancellationToken cancellationToken,
        (string Key, string Value)? extraCondition = null)
    {
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
            return new EventSubDiagnosticsResult(false, [], 0, 0, "Twitch credentials are not configured.");
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(options.DefaultClientId, options.OAuthClientSecret, cancellationToken);
        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            return new EventSubDiagnosticsResult(true, [], 0, 0, $"Failed to obtain app access token: {appToken.ErrorMessage}");
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var result = await twitchApiClient.GetEventSubSubscriptionsAsync(auth, cancellationToken);
        if (!result.IsSuccess)
        {
            return new EventSubDiagnosticsResult(true, [], 0, 0, $"Failed to list EventSub subscriptions: {result.ErrorMessage}");
        }

        var statuses = result.Subscriptions
            .Select(s => new EventSubSubscriptionStatus(s.Id, s.Status, s.Type, s.Version, s.Condition, s.CreatedAt))
            .ToList();

        return new EventSubDiagnosticsResult(true, statuses, result.TotalCost, result.MaxTotalCost, null);
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
        var deletedCount = 0;
        var listResult = await twitchApiClient.GetEventSubSubscriptionsAsync(auth, cancellationToken);
        if (listResult.IsSuccess)
        {
            foreach (var sub in listResult.Subscriptions)
            {
                var deleted = await twitchApiClient.DeleteEventSubSubscriptionAsync(sub.Id, auth, cancellationToken);
                if (deleted)
                {
                    deletedCount++;
                    logger.LogInformation("Deleted EventSub subscription {Id} ({Type}) during force resync.", sub.Id, sub.Type);
                }
                else
                {
                    logger.LogWarning("Failed to delete EventSub subscription {Id} ({Type}) during force resync.", sub.Id, sub.Type);
                }
            }
        }
        else
        {
            logger.LogWarning("Unable to list EventSub subscriptions before force resync. {ErrorMessage}", listResult.ErrorMessage);
        }

        var ensureSummary = await EnsureSubscriberSubscriptionsCoreAsync(cancellationToken);
        var message = ensureSummary.Message;

        logger.LogInformation(
            "Force resync of EventSub subscriptions completed. Deleted={DeletedCount}, Ensured={EnsuredCount}, AlreadyExists={AlreadyExistsCount}, Failed={FailedCount}",
            deletedCount,
            ensureSummary.EnsuredCount,
            ensureSummary.AlreadyExistsCount,
            ensureSummary.FailedCount);

        return new EventSubForceResyncResult(
            deletedCount,
            ensureSummary.EnsuredCount,
            ensureSummary.AlreadyExistsCount,
            ensureSummary.FailedCount,
            message);
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
        var hmac = ComputeHmac(secret, message);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hmac),
            Encoding.UTF8.GetBytes(messageSignature));
    }

    private static string ComputeHmac(string secret, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
