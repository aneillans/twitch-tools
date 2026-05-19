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
    ILogger<TwitchEventSubService> logger) : ITwitchEventSubService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        if (featureFlags.Value.DisableExternalPosting)
        {
            logger.LogInformation("Skipping EventSub subscription bootstrap because FeatureFlags:DisableExternalPosting is enabled.");
            return;
        }

        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId)
            || string.IsNullOrWhiteSpace(options.OAuthClientSecret)
            || string.IsNullOrWhiteSpace(options.EventSubCallbackUrl)
            || string.IsNullOrWhiteSpace(options.EventSubSecret))
        {
            logger.LogDebug("Skipping EventSub subscription bootstrap because Twitch EventSub is not fully configured.");
            return;
        }

        var callbackUri = new Uri(options.EventSubCallbackUrl, UriKind.Absolute);
        if (!string.Equals(callbackUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Skipping EventSub subscription bootstrap because callback URL is not HTTPS: {CallbackUrl}", options.EventSubCallbackUrl);
            return;
        }

        var appToken = await twitchApiClient.GetAppAccessTokenAsync(
            options.DefaultClientId,
            options.OAuthClientSecret,
            cancellationToken);

        if (!appToken.IsSuccess || string.IsNullOrWhiteSpace(appToken.AccessToken))
        {
            logger.LogWarning("Unable to obtain an app access token for EventSub subscription bootstrap. {ErrorMessage}", appToken.ErrorMessage);
            return;
        }

        var auth = new TwitchAuthContext(options.DefaultClientId, appToken.AccessToken, null, null);
        var streamers = await dbContext.Streamers
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.TwitchUserId) && !string.IsNullOrWhiteSpace(x.TwitchStreamerAccessToken))
            .ToListAsync(cancellationToken);

        foreach (var streamer in streamers)
        {
            await EnsureSubscriptionTypeAsync(streamer, auth, options, "channel.subscribe", cancellationToken);
            await EnsureSubscriptionTypeAsync(streamer, auth, options, "channel.subscription.message", cancellationToken);
            await TryPrefillSubscriberSnapshotAsync(streamer, options, cancellationToken);
        }
    }

    private async Task EnsureSubscriptionTypeAsync(
        Streamer streamer,
        TwitchAuthContext appAuth,
        TwitchOptions options,
        string eventSubType,
        CancellationToken cancellationToken)
    {
        var result = await twitchApiClient.CreateEventSubSubscriptionAsync(
            appAuth,
            new TwitchEventSubSubscriptionRequest(
                eventSubType,
                "1",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["broadcaster_user_id"] = streamer.TwitchUserId
                },
                new TwitchEventSubTransport(
                    "webhook",
                    options.EventSubCallbackUrl,
                    options.EventSubSecret)),
            cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Ensured EventSub subscription {EventSubType} for {Streamer}.", eventSubType, streamer.DisplayName);
            return;
        }

        if (result.IsAlreadyExists)
        {
            logger.LogDebug("EventSub subscription {EventSubType} already exists for {Streamer}.", eventSubType, streamer.DisplayName);
            return;
        }

        logger.LogWarning(
            "Failed to ensure EventSub subscription {EventSubType} for {Streamer}. StatusCode={StatusCode}. Error={ErrorMessage}",
            eventSubType,
            streamer.DisplayName,
            result.StatusCode,
            result.ErrorMessage);
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
