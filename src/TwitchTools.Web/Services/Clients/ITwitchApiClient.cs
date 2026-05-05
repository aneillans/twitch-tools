namespace TwitchTools.Web.Services.Clients;

public interface ITwitchApiClient
{
    Task<TwitchTokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken);
    Task<TwitchTokenRefreshResult> RefreshAccessTokenAsync(string refreshToken, string clientId, string clientSecret, CancellationToken cancellationToken);
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

public sealed record TwitchScheduleSegment(
    string SegmentId,
    string Title,
    string? CategoryName,
    string BroadcasterLogin,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc);

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