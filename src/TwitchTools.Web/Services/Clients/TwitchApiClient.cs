using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchTools.Web.Services.Clients;

public sealed class TwitchApiClient(
    HttpClient httpClient,
    ILogger<TwitchApiClient> logger) : ITwitchApiClient
{
    private static readonly JsonSerializerOptions TwitchJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TwitchTokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TwitchTokenValidationResult(false, null, null, null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TwitchTokenValidateResponse>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);

        return new TwitchTokenValidationResult(
            true,
            payload?.ClientId,
            payload?.Login,
            payload?.UserId,
            payload?.ExpiresIn,
            null);
    }

    public async Task<TwitchTokenRefreshResult> RefreshAccessTokenAsync(string refreshToken, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TwitchTokenRefreshResult(false, null, null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TwitchRefreshTokenResponse>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);

        return new TwitchTokenRefreshResult(
            true,
            payload?.AccessToken,
            payload?.RefreshToken,
            payload?.ExpiresIn,
            null);
    }

    public async Task<TwitchAppAccessTokenResult> GetAppAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials"
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TwitchAppAccessTokenResult(false, null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TwitchClientCredentialsResponse>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);

        return new TwitchAppAccessTokenResult(true, payload?.AccessToken, payload?.ExpiresIn, null);
    }

    public async Task<TwitchEventSubCreateResult> CreateEventSubSubscriptionAsync(
        TwitchAuthContext authContext,
        TwitchEventSubSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "helix/eventsub/subscriptions", authContext);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                type = request.Type,
                version = request.Version,
                condition = request.Condition,
                transport = new
                {
                    method = request.Transport.Method,
                    callback = request.Transport.Callback,
                    secret = request.Transport.Secret
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new TwitchEventSubCreateResult(true, false, (int)response.StatusCode, null);
        }

        if ((int)response.StatusCode == 409)
        {
            return new TwitchEventSubCreateResult(false, true, (int)response.StatusCode, null);
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "Twitch EventSub subscription create failed: {StatusCode}. Response: {ResponseBody}",
            response.StatusCode,
            errorBody);
        return new TwitchEventSubCreateResult(false, false, (int)response.StatusCode, errorBody);
    }

    public async Task<TwitchEventSubListResult> GetEventSubSubscriptionsAsync(TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        var allSubs = new List<TwitchEventSubSubscriptionInfo>();
        string? cursor = null;
        var totalCost = 0;
        var maxTotalCost = 0;

        do
        {
            var url = cursor is null
                ? "helix/eventsub/subscriptions?first=100"
                : $"helix/eventsub/subscriptions?first=100&after={Uri.EscapeDataString(cursor)}";

            using var request = CreateRequest(HttpMethod.Get, url, authContext);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Twitch EventSub list failed: {StatusCode}. Response: {ResponseBody}", response.StatusCode, errorBody);
                return new TwitchEventSubListResult(false, [], 0, 0, errorBody);
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TwitchEventSubListResponse>(contentStream, TwitchJsonOptions, cancellationToken);
            if (payload?.Data is null) break;

            totalCost = payload.TotalCost;
            maxTotalCost = payload.MaxTotalCost;

            foreach (var item in payload.Data)
            {
                allSubs.Add(new TwitchEventSubSubscriptionInfo(
                    item.Id ?? string.Empty,
                    item.Status ?? string.Empty,
                    item.Type ?? string.Empty,
                    item.Version ?? string.Empty,
                    (IReadOnlyDictionary<string, string>?)item.Condition ?? new Dictionary<string, string>(),
                    item.CreatedAt));
            }

            cursor = payload.Pagination?.Cursor;
        } while (cursor is not null);

        return new TwitchEventSubListResult(true, allSubs, totalCost, maxTotalCost, null);
    }

    public async Task<bool> DeleteEventSubSubscriptionAsync(string subscriptionId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"helix/eventsub/subscriptions?id={Uri.EscapeDataString(subscriptionId)}", authContext);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return (int)response.StatusCode == 204;
    }

    public async Task<bool> IsStreamerLiveAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        var status = await GetStreamStatusAsync(broadcasterUserId, authContext, cancellationToken);
        return status.IsLive;
    }

    public async Task<TwitchStreamStatus> GetStreamStatusAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"helix/streams?user_id={Uri.EscapeDataString(broadcasterUserId)}", authContext);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Twitch streams check failed for {Broadcaster}: {StatusCode}. Response: {ResponseBody}",
                broadcasterUserId,
                response.StatusCode,
                errorBody);
            return new TwitchStreamStatus(false, null, null);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchStreamItem>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        var stream = payload?.Data.FirstOrDefault();
        if (stream is null)
        {
            logger.LogDebug("Twitch streams response for {Broadcaster} returned no live stream.", broadcasterUserId);
            return new TwitchStreamStatus(false, null, null);
        }

        logger.LogDebug(
            "Twitch streams response for {Broadcaster}: IsLive={IsLive}, Title={Title}, GameName={GameName}",
            broadcasterUserId,
            true,
            stream.Title,
            stream.GameName);

        return new TwitchStreamStatus(true, stream.Title, stream.GameName);
    }

    public async Task<IReadOnlyCollection<string>> GetCurrentChattersAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authContext.ModeratorUserId))
        {
            return Array.Empty<string>();
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"helix/chat/chatters?broadcaster_id={Uri.EscapeDataString(broadcasterUserId)}&moderator_id={Uri.EscapeDataString(authContext.ModeratorUserId)}",
            authContext);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Twitch chatter poll failed for {Broadcaster}: {StatusCode}. Response: {ResponseBody}",
                broadcasterUserId,
                response.StatusCode,
                errorBody);
            return Array.Empty<string>();
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchChatter>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        return payload?.Data.Select(x => x.UserId).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
    }

    public async Task SendChatMessageAsync(string broadcasterUserId, string message, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authContext.BotUserId))
        {
            logger.LogWarning("Skipping chat send because BotUserId is not configured.");
            return;
        }

        using var request = CreateRequest(HttpMethod.Post, "helix/chat/messages", authContext);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                broadcaster_id = broadcasterUserId,
                sender_id = authContext.BotUserId,
                message
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twitch chat message failed for {Broadcaster}: {StatusCode}", broadcasterUserId, response.StatusCode);
        }
    }

    public async Task<IReadOnlyCollection<TwitchScheduleSegment>> GetScheduleAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"helix/schedule?broadcaster_id={Uri.EscapeDataString(broadcasterUserId)}", authContext);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twitch schedule fetch failed for {Broadcaster}: {StatusCode}", broadcasterUserId, response.StatusCode);
            return Array.Empty<TwitchScheduleSegment>();
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TwitchScheduleResponse>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        var data = payload?.Data;
        var segments = data?.Segments ?? [];
        var broadcasterLogin = string.IsNullOrWhiteSpace(data?.BroadcasterLogin) ? broadcasterUserId : data.BroadcasterLogin;

        return segments
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new TwitchScheduleSegment(
                x.Id!,
                x.Title!,
                x.Category?.Name,
                broadcasterLogin,
                x.StartTime,
                x.EndTime))
            .ToArray();
    }

    public async Task<TwitchFollowerEvent?> GetLatestFollowerAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authContext);

        if (string.IsNullOrWhiteSpace(authContext.ModeratorUserId))
        {
            return null;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"helix/channels/followers?broadcaster_id={Uri.EscapeDataString(broadcasterUserId)}&moderator_id={Uri.EscapeDataString(authContext.ModeratorUserId)}&first=1",
            authContext);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Twitch follower fetch failed for {Broadcaster}: {StatusCode}. Response: {ResponseBody}",
                broadcasterUserId,
                response.StatusCode,
                errorBody);
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchFollowerItem>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        var item = payload?.Data.FirstOrDefault();
        if (item is null || string.IsNullOrWhiteSpace(item.UserId))
        {
            logger.LogDebug("Twitch follower response for {Broadcaster} returned no follower.", broadcasterUserId);
            return null;
        }

        logger.LogDebug(
            "Twitch follower response for {Broadcaster}: UserId={UserId}, UserLogin={UserLogin}, UserName={UserName}, FollowedAt={FollowedAtUtc}",
            broadcasterUserId,
            item.UserId,
            item.UserLogin,
            item.UserName,
            item.FollowedAt);

        return new TwitchFollowerEvent(item.UserId, item.UserLogin, item.UserName, item.FollowedAt);
    }

    public async Task<TwitchSubscriberEvent?> GetLatestSubscriberAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"helix/subscriptions?broadcaster_id={Uri.EscapeDataString(broadcasterUserId)}&first=1",
            authContext);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Twitch subscriber fetch failed for {Broadcaster}: {StatusCode}. Response: {ResponseBody}",
                broadcasterUserId,
                response.StatusCode,
                errorBody);
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchSubscriberItem>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        var item = payload?.Data.FirstOrDefault();
        if (item is null || string.IsNullOrWhiteSpace(item.UserId))
        {
            logger.LogDebug("Twitch subscriber response for {Broadcaster} returned no subscriber.", broadcasterUserId);
            return null;
        }

        logger.LogDebug(
            "Twitch subscriber response for {Broadcaster}: UserId={UserId}, UserLogin={UserLogin}, UserName={UserName}",
            broadcasterUserId,
            item.UserId,
            item.UserLogin,
            item.UserName);

        return new TwitchSubscriberEvent(item.UserId, item.UserLogin, item.UserName);
    }

    public async Task<TwitchAuthorizationLookupResult> GetAuthorizationsByUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        TwitchAuthContext authContext,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new TwitchAuthorizationLookupResult(
                true,
                new Dictionary<string, TwitchUserAuthorization>(StringComparer.Ordinal),
                null);
        }

        var distinctIds = userIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctIds.Length == 0)
        {
            return new TwitchAuthorizationLookupResult(
                true,
                new Dictionary<string, TwitchUserAuthorization>(StringComparer.Ordinal),
                null);
        }

        // Twitch limits authorization/users lookups to 10 user_id values per request.
        var results = new Dictionary<string, TwitchUserAuthorization>(StringComparer.Ordinal);
        foreach (var batch in distinctIds.Chunk(10))
        {
            var query = string.Join("&", batch.Select(x => $"user_id={Uri.EscapeDataString(x)}"));
            using var request = CreateRequest(HttpMethod.Get, $"helix/authorization/users?{query}", authContext);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Twitch authorization lookup failed: {StatusCode}. Response: {ResponseBody}",
                    response.StatusCode,
                    errorBody);
                return new TwitchAuthorizationLookupResult(
                    false,
                    new Dictionary<string, TwitchUserAuthorization>(StringComparer.Ordinal),
                    errorBody);
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchAuthorizationUserItem>>(
                contentStream,
                TwitchJsonOptions,
                cancellationToken: cancellationToken);

            foreach (var item in payload?.Data ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.UserId))
                {
                    continue;
                }

                var scopes = (item.Scopes ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                results[item.UserId] = new TwitchUserAuthorization(
                    item.UserId,
                    item.UserLogin,
                    item.UserName,
                    scopes);
            }
        }

        return new TwitchAuthorizationLookupResult(true, results, null);
    }

    public async Task<IReadOnlyDictionary<string, TwitchUserProfile>> GetUsersByIdsAsync(
        IReadOnlyCollection<string> userIds,
        TwitchAuthContext authContext,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, TwitchUserProfile>(StringComparer.Ordinal);
        }

        var distinctIds = userIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctIds.Length == 0)
        {
            return new Dictionary<string, TwitchUserProfile>(StringComparer.Ordinal);
        }

        // Twitch users endpoint supports batching by repeating id query values.
        var query = string.Join("&", distinctIds.Select(x => $"id={Uri.EscapeDataString(x)}"));
        using var request = CreateRequest(HttpMethod.Get, $"helix/users?{query}", authContext);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Twitch user lookup failed: {StatusCode}. Response: {ResponseBody}",
                response.StatusCode,
                errorBody);
            return new Dictionary<string, TwitchUserProfile>(StringComparer.Ordinal);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<TwitchUserItem>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);

        return payload?.Data
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(
                x => x.Id,
                x => new TwitchUserProfile(x.Id, x.Login, x.DisplayName),
                StringComparer.Ordinal)
            ?? new Dictionary<string, TwitchUserProfile>(StringComparer.Ordinal);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, TwitchAuthContext authContext)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.AccessToken);
        request.Headers.Add("Client-Id", authContext.ClientId);
        return request;
    }

    private sealed class TwitchEventSubListResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchEventSubListItem> Data { get; init; } = [];

        [JsonPropertyName("total_cost")]
        public int TotalCost { get; init; }

        [JsonPropertyName("max_total_cost")]
        public int MaxTotalCost { get; init; }

        [JsonPropertyName("pagination")]
        public TwitchEventSubPagination? Pagination { get; init; }
    }

    private sealed class TwitchEventSubListItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("condition")]
        public Dictionary<string, string>? Condition { get; init; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; init; }
    }

    private sealed class TwitchEventSubPagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }

    private sealed class HelixDataEnvelope<T>
    {
        [JsonPropertyName("data")]
        public List<T> Data { get; init; } = [];
    }

    private sealed class TwitchChatter
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;
    }

    private sealed class TwitchStreamItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("game_name")]
        public string? GameName { get; init; }
    }

    private sealed class TwitchScheduleResponse
    {
        [JsonPropertyName("data")]
        public TwitchScheduleData? Data { get; init; }
    }

    private sealed class TwitchScheduleData
    {
        [JsonPropertyName("segments")]
        public List<TwitchScheduleItem> Segments { get; init; } = [];

        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; init; } = string.Empty;
    }

    private sealed class TwitchScheduleItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("start_time")]
        public DateTimeOffset StartTime { get; init; }

        [JsonPropertyName("end_time")]
        public DateTimeOffset EndTime { get; init; }

        [JsonPropertyName("category")]
        public TwitchScheduleCategory? Category { get; init; }
    }

    private sealed class TwitchScheduleCategory
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class TwitchFollowerItem
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("user_login")]
        public string? UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; init; }

        [JsonPropertyName("followed_at")]
        public DateTimeOffset? FollowedAt { get; init; }
    }

    private sealed class TwitchSubscriberItem
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("user_login")]
        public string? UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; init; }
    }

    private sealed class TwitchTokenValidateResponse
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; init; }

        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class TwitchRefreshTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class TwitchClientCredentialsResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class TwitchUserItem
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }
    }

    private sealed class TwitchAuthorizationUserItem
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("user_login")]
        public string? UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; init; }

        [JsonPropertyName("scopes")]
        public List<string>? Scopes { get; init; }
    }
}