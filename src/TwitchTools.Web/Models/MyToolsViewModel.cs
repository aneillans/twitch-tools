namespace TwitchTools.Web.Models;

public sealed class MyToolsViewModel
{
    public bool HasProfile { get; init; }
    public TwitchConnectionInput Twitch { get; init; } = new();
    public BlueSkyConnectionInput BlueSky { get; init; } = new();
    public IReadOnlyCollection<DiscordSyncItem> DiscordSyncs { get; init; } = [];
}

public sealed class TimedMessagesPageViewModel
{
    public IReadOnlyCollection<TimedMessageItem> TimedMessages { get; init; } = [];
}

public sealed class OverlaySettingsViewModel
{
    public string FollowerOverlayUrl { get; init; } = string.Empty;
    public string SubscriberOverlayUrl { get; init; } = string.Empty;
    public string FollowerOverlayToken { get; init; } = string.Empty;
    public string SubscriberOverlayToken { get; init; } = string.Empty;
}

public sealed class TwitchConnectionInput
{
    public string DisplayName { get; set; } = string.Empty;
    public string TwitchUserId { get; set; } = string.Empty;
    public string TwitchStreamerAccessToken { get; set; } = string.Empty;
    public string? TwitchStreamerRefreshToken { get; set; }
    public string? TwitchClientId { get; set; }
    public string? TwitchBotAccessToken { get; set; }
    public string? TwitchBotRefreshToken { get; set; }
}

public sealed class BlueSkyConnectionInput
{
    public string? BlueSkyIdentifier { get; set; }
    public string? BlueSkyAppPassword { get; set; }
}

public sealed class AddTimedMessageInput
{
    public string MessageText { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 30;
}

public sealed class TimedMessageItem
{
    public long Id { get; init; }
    public string MessageText { get; init; } = string.Empty;
    public int IntervalMinutes { get; init; }
    public bool Enabled { get; init; }
    public DateTime? LastSentUtc { get; init; }
}

public sealed class AddDiscordSyncInput
{
    public string GuildId { get; set; } = string.Empty;
}

public sealed class DiscordSyncItem
{
    public long Id { get; init; }
    public string GuildId { get; init; } = string.Empty;
    public DateTime LastSyncedUtc { get; init; }
}
