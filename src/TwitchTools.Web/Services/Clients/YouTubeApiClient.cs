using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Services.Clients;

public sealed class YouTubeApiClient(
    HttpClient httpClient,
    IOptions<YouTubeOptions> youTubeOptions,
    ILogger<YouTubeApiClient> logger) : IYouTubeApiClient
{
    private static readonly JsonSerializerOptions YouTubeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<YouTubeTokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        var options = youTubeOptions.Value;
        var requestUri = new Uri($"{options.TokenInfoUri}?access_token={Uri.EscapeDataString(accessToken)}", UriKind.Absolute);
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new YouTubeTokenValidationResult(false, null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeTokenInfoResponse>(
            contentStream,
            YouTubeJsonOptions,
            cancellationToken: cancellationToken);

        return new YouTubeTokenValidationResult(true, payload?.ExpiresIn, payload?.Scope, null);
    }

    public async Task<YouTubeTokenRefreshResult> RefreshAccessTokenAsync(
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var options = youTubeOptions.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUri)
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
            return new YouTubeTokenRefreshResult(false, null, null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeTokenRefreshResponse>(
            contentStream,
            YouTubeJsonOptions,
            cancellationToken: cancellationToken);

        // Google typically does not re-issue a refresh token on a refresh_token grant; callers
        // should keep the existing refresh token when this comes back null.
        return new YouTubeTokenRefreshResult(true, payload?.AccessToken, payload?.RefreshToken, payload?.ExpiresIn, null);
    }

    public async Task<YouTubeChannelInfo?> GetChannelAsync(YouTubeAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "channels?part=snippet&mine=true", authContext);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("YouTube channel lookup failed: {StatusCode}. Response: {ResponseBody}", response.StatusCode, errorBody);
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeListEnvelope<YouTubeChannelItem>>(
            contentStream,
            YouTubeJsonOptions,
            cancellationToken: cancellationToken);
        var channel = payload?.Items.FirstOrDefault();
        if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
        {
            return null;
        }

        return new YouTubeChannelInfo(channel.Id, channel.Snippet?.Title);
    }

    public async Task<YouTubeLiveBroadcastInfo?> GetActiveLiveBroadcastAsync(YouTubeAuthContext authContext, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "liveBroadcasts?part=snippet&broadcastStatus=active&broadcastType=all&mine=true",
            authContext);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("YouTube live broadcast lookup failed: {StatusCode}. Response: {ResponseBody}", response.StatusCode, errorBody);
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeListEnvelope<YouTubeLiveBroadcastItem>>(
            contentStream,
            YouTubeJsonOptions,
            cancellationToken: cancellationToken);
        var broadcast = payload?.Items.FirstOrDefault();
        if (broadcast is null || string.IsNullOrWhiteSpace(broadcast.Id) || string.IsNullOrWhiteSpace(broadcast.Snippet?.LiveChatId))
        {
            return null;
        }

        return new YouTubeLiveBroadcastInfo(broadcast.Id, broadcast.Snippet.LiveChatId, broadcast.Snippet.Title);
    }

    public async Task<YouTubeChatMessagesResult> GetLiveChatMessagesAsync(
        string liveChatId,
        string? pageToken,
        YouTubeAuthContext authContext,
        CancellationToken cancellationToken)
    {
        var url = $"liveChat/messages?part=snippet,authorDetails&liveChatId={Uri.EscapeDataString(liveChatId)}";
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, authContext);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "YouTube live chat message poll failed for {LiveChatId}: {StatusCode}. Response: {ResponseBody}",
                liveChatId,
                response.StatusCode,
                errorBody);
            return new YouTubeChatMessagesResult(false, [], null, null, errorBody);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeLiveChatMessagesResponse>(
            contentStream,
            YouTubeJsonOptions,
            cancellationToken: cancellationToken);

        var messages = (payload?.Items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Snippet?.DisplayMessage))
            .Select(x => new YouTubeChatMessage(
                x.Id!,
                x.AuthorDetails?.ChannelId,
                x.AuthorDetails?.DisplayName,
                x.Snippet!.DisplayMessage!,
                x.Snippet.PublishedAt))
            .ToArray();

        return new YouTubeChatMessagesResult(true, messages, payload?.NextPageToken, payload?.PollingIntervalMillis, null);
    }

    public async Task<bool> SendLiveChatMessageAsync(
        string liveChatId,
        string message,
        YouTubeAuthContext authContext,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "liveChat/messages?part=snippet", authContext);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                snippet = new
                {
                    liveChatId,
                    type = "textMessageEvent",
                    textMessageDetails = new { messageText = message }
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "YouTube live chat message send failed for {LiveChatId}: {StatusCode}. Response: {ResponseBody}",
                liveChatId,
                response.StatusCode,
                errorBody);
            return false;
        }

        return true;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, YouTubeAuthContext authContext)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.AccessToken);
        return request;
    }

    private sealed class YouTubeTokenInfoResponse
    {
        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class YouTubeTokenRefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class YouTubeListEnvelope<T>
    {
        [JsonPropertyName("items")]
        public List<T> Items { get; init; } = [];
    }

    private sealed class YouTubeChannelItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("snippet")]
        public YouTubeChannelSnippet? Snippet { get; init; }
    }

    private sealed class YouTubeChannelSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }

    private sealed class YouTubeLiveBroadcastItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("snippet")]
        public YouTubeLiveBroadcastSnippet? Snippet { get; init; }
    }

    private sealed class YouTubeLiveBroadcastSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("liveChatId")]
        public string? LiveChatId { get; init; }
    }

    private sealed class YouTubeLiveChatMessagesResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeLiveChatMessageItem> Items { get; init; } = [];

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }

        [JsonPropertyName("pollingIntervalMillis")]
        public int? PollingIntervalMillis { get; init; }
    }

    private sealed class YouTubeLiveChatMessageItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("snippet")]
        public YouTubeLiveChatMessageSnippet? Snippet { get; init; }

        [JsonPropertyName("authorDetails")]
        public YouTubeLiveChatAuthorDetails? AuthorDetails { get; init; }
    }

    private sealed class YouTubeLiveChatMessageSnippet
    {
        [JsonPropertyName("displayMessage")]
        public string? DisplayMessage { get; init; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; init; }
    }

    private sealed class YouTubeLiveChatAuthorDetails
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }
    }
}
