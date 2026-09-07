namespace TwitchTools.Web.Services.Clients;

public interface IYouTubeApiClient
{
    Task<YouTubeTokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken);

    Task<YouTubeTokenRefreshResult> RefreshAccessTokenAsync(
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken);

    Task<YouTubeChannelInfo?> GetChannelAsync(YouTubeAuthContext authContext, CancellationToken cancellationToken);

    Task<YouTubeLiveBroadcastInfo?> GetActiveLiveBroadcastAsync(YouTubeAuthContext authContext, CancellationToken cancellationToken);

    Task<YouTubeChatMessagesResult> GetLiveChatMessagesAsync(
        string liveChatId,
        string? pageToken,
        YouTubeAuthContext authContext,
        CancellationToken cancellationToken);

    Task<bool> SendLiveChatMessageAsync(
        string liveChatId,
        string message,
        YouTubeAuthContext authContext,
        CancellationToken cancellationToken);
}

public sealed record YouTubeAuthContext(string AccessToken);

public sealed record YouTubeTokenValidationResult(
    bool IsValid,
    int? ExpiresInSeconds,
    string? Scope,
    string? ErrorMessage);

public sealed record YouTubeTokenRefreshResult(
    bool IsSuccess,
    string? AccessToken,
    string? RefreshToken,
    int? ExpiresInSeconds,
    string? ErrorMessage);

public sealed record YouTubeChannelInfo(string ChannelId, string? Title);

public sealed record YouTubeLiveBroadcastInfo(string VideoId, string LiveChatId, string? Title);

public sealed record YouTubeChatMessage(
    string MessageId,
    string? AuthorChannelId,
    string? AuthorDisplayName,
    string MessageText,
    DateTimeOffset? PublishedAtUtc);

public sealed record YouTubeChatMessagesResult(
    bool IsSuccess,
    IReadOnlyList<YouTubeChatMessage> Messages,
    string? NextPageToken,
    int? PollingIntervalMillis,
    string? ErrorMessage);
