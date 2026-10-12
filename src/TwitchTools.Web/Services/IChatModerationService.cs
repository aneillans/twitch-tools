using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

public interface IChatModerationService
{
    Task<ChatModerationResult> ModerateAsync(Streamer streamer, ChatModerationRequest request, CancellationToken cancellationToken);
}

public enum ChatModerationAction
{
    Delete,
    Timeout,
    Ban
}

public sealed record ChatModerationRequest(
    string Platform,
    ChatModerationAction Action,
    string? MessageId,
    string? UserId,
    int? DurationSeconds);

public sealed record ChatModerationResult(bool IsSuccess, string Message);
