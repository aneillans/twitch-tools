namespace TwitchTools.Web.Services.Clients;

public interface ITwitchApiClient
{
    Task<bool> IsStreamerLiveAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetCurrentChattersAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task SendChatMessageAsync(string broadcasterUserId, string message, TwitchAuthContext authContext, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TwitchScheduleSegment>> GetScheduleAsync(string broadcasterUserId, TwitchAuthContext authContext, CancellationToken cancellationToken);
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