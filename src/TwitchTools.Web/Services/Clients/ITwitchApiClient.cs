namespace TwitchTools.Web.Services.Clients;

public interface ITwitchApiClient
{
    Task<TwitchTokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken);
    Task<TwitchTokenRefreshResult> RefreshAccessTokenAsync(string refreshToken, string clientId, string clientSecret, CancellationToken cancellationToken);
    Task<TwitchAppAccessTokenResult> GetAppAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken);
    Task<TwitchEventSubCreateResult> CreateEventSubSubscriptionAsync(
        TwitchAuthContext authContext,
        TwitchEventSubSubscriptionRequest request,
        CancellationToken cancellationToken);
    Task<TwitchEventSubListResult> GetEventSubSubscriptionsAsync(TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<bool> DeleteEventSubSubscriptionAsync(string subscriptionId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<TwitchStreamStatus> GetStreamStatusAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<bool> IsStreamerLiveAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetCurrentChattersAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task SendChatMessageAsync(string broadcasterUserId, string message, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TwitchScheduleSegment>> GetScheduleAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<TwitchFollowerEvent?> GetLatestFollowerAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<TwitchSubscriberEvent?> GetLatestSubscriberAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, TwitchUserProfile>> GetUsersByIdsAsync(
        IReadOnlyCollection<string> userIds,
        TwitchAuthContext authContext,
        CancellationToken cancellationToken);
}

public sealed record TwitchAuthContext(
    string ClientId,
    string AccessToken,
    string? BotUserId,
    string? ModeratorUserId);

public sealed record TwitchEventSubSubscriptionRequest(
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition,
    TwitchEventSubTransport Transport);

public sealed record TwitchEventSubTransport(
    string Method,
    string Callback,
    string Secret);

public sealed record TwitchEventSubCreateResult(
    bool IsSuccess,
    bool IsAlreadyExists,
    int StatusCode,
    string? ErrorMessage);

public sealed record TwitchEventSubListResult(
    bool IsSuccess,
    IReadOnlyList<TwitchEventSubSubscriptionInfo> Subscriptions,
    int TotalCost,
    int MaxTotalCost,
    string? ErrorMessage);

public sealed record TwitchEventSubSubscriptionInfo(
    string Id,
    string Status,
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition,
    DateTime CreatedAt);

public sealed record TwitchAppAccessTokenResult(
    bool IsSuccess,
    string? AccessToken,
    int? ExpiresInSeconds,
    string? ErrorMessage);

public sealed record TwitchScheduleSegment(
    string SegmentId,
    string Title,
    string? CategoryName,
    string BroadcasterLogin,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc);

public sealed record TwitchStreamStatus(
    bool IsLive,
    string? StreamTitle,
    string? GameName);

public sealed record TwitchFollowerEvent(
    string UserId,
    string? UserLogin,
    string? UserName,
    DateTimeOffset? FollowedAtUtc);

public sealed record TwitchSubscriberEvent(
    string UserId,
    string? UserLogin,
    string? UserName);

public sealed record TwitchUserProfile(
    string UserId,
    string? UserLogin,
    string? DisplayName);

public sealed record TwitchTokenValidationResult(
    bool IsValid,
    string? ClientId,
    string? Login,
    string? UserId,
    int? ExpiresInSeconds,
    string? ErrorMessage);

public sealed record TwitchTokenRefreshResult(
    bool IsSuccess,
    string? AccessToken,
    string? RefreshToken,
    int? ExpiresInSeconds,
    string? ErrorMessage);