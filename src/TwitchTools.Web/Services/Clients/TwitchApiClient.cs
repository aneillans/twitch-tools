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

    public async Task<bool> IsStreamerLiveAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken)
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
            return false;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<HelixDataEnvelope<JsonElement>>(
            contentStream,
            TwitchJsonOptions,
            cancellationToken: cancellationToken);
        return payload?.Data.Count > 0;
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
            return null;
        }

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
            return null;
        }

        return new TwitchSubscriberEvent(item.UserId, item.UserLogin, item.UserName);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, TwitchAuthContext authContext)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.AccessToken);
        request.Headers.Add("Client-Id", authContext.ClientId);
        return request;
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
}