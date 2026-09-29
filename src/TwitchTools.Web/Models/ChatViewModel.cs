using TwitchTools.Web.Services;

namespace TwitchTools.Web.Models;

public sealed class ChatPageViewModel
{
    public bool TwitchConnected { get; init; }
    public bool YouTubeConnected { get; init; }
}

public sealed class ChatModerationInput
{
    public string Platform { get; set; } = string.Empty;
    public ChatModerationAction Action { get; set; }
    public string? MessageId { get; set; }
    public string? UserId { get; set; }
    public int? DurationSeconds { get; set; }
}
