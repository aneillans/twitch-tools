namespace TwitchTools.Web.Models;

public sealed class MyToolsViewModel
{
    public bool HasProfile { get; init; }
    public IReadOnlyCollection<string> TwitchGrantWarnings { get; init; } = [];
    public TwitchConnectionInput Twitch { get; init; } = new();
    public YouTubeConnectionInput YouTube { get; init; } = new();
    public BlueSkyConnectionInput BlueSky { get; init; } = new();
}

public sealed class LiveAutomationViewModel
{
    public IReadOnlyCollection<DiscordSyncItem> DiscordSyncs { get; init; } = [];
    public bool IsBlueSkyConfigured { get; init; }
    public BlueSkyLivePostTemplatesInput BlueSkyTemplates { get; init; } = new();
    public bool IsCrossPostConfigured { get; init; }
    public CrossPostSettingsInput CrossPostSettings { get; init; } = new();
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
    public CustomOverlayWidgetInput CustomWidget { get; set; } = new();
    public string? CustomWidgetOverlayUrl { get; init; }
    public string? CustomWidgetOverlayToken { get; init; }
    public DateTime? CustomWidgetUpdatedUtc { get; init; }
}

public sealed class CustomOverlayWidgetInput
{
    public string Name { get; set; } = "Imported StreamElements Widget";
    public string Html { get; set; } = string.Empty;
    public string Css { get; set; } = string.Empty;
    public string Js { get; set; } = string.Empty;
    public string FieldsJson { get; set; } = "{}";
    public string DataJson { get; set; } = "{}";
}

public sealed class CustomOverlayWidgetRuntimeViewModel
{
    public string OverlayToken { get; init; } = string.Empty;
    public string WidgetName { get; init; } = string.Empty;
    public string Html { get; init; } = string.Empty;
    public string Css { get; init; } = string.Empty;
    public string Js { get; init; } = string.Empty;
    public string FieldDataJson { get; init; } = "{}";
    public string ChannelProviderId { get; init; } = "0";
}

public sealed class TwitchConnectionInput
{
    public string DisplayName { get; set; } = string.Empty;
    public string TwitchUserId { get; set; } = string.Empty;
    public string? TwitchBotUserId { get; set; }
    public TwitchTokenStatusViewModel StreamerTokenStatus { get; init; } = TwitchTokenStatusViewModel.Missing();
    public TwitchTokenStatusViewModel BotTokenStatus { get; init; } = TwitchTokenStatusViewModel.Missing();
    public string TwitchStreamerAccessToken { get; set; } = string.Empty;
    public string? TwitchStreamerRefreshToken { get; set; }
    public string? TwitchClientId { get; set; }
    public string? TwitchBotAccessToken { get; set; }
    public string? TwitchBotRefreshToken { get; set; }
}

public sealed class TwitchTokenStatusViewModel
{
    public string State { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string TextClass { get; init; } = "text-muted";

    public static TwitchTokenStatusViewModel Missing() => new()
    {
        State = "Missing",
        Details = "No access token saved.",
        TextClass = "text-muted"
    };
}

public sealed class YouTubeConnectionInput
{
    public string? YouTubeChannelId { get; set; }
    public string? YouTubeChannelTitle { get; set; }
    public string? YouTubeBotChannelId { get; set; }
    public TwitchTokenStatusViewModel StreamerTokenStatus { get; init; } = TwitchTokenStatusViewModel.Missing();
    public TwitchTokenStatusViewModel BotTokenStatus { get; init; } = TwitchTokenStatusViewModel.Missing();
}

public sealed class CrossPostSettingsInput
{
    public bool CrossPostChatEnabled { get; set; }
    public string? CrossPostToTwitchTemplate { get; set; }
    public string? CrossPostToYouTubeTemplate { get; set; }
}

public sealed class BlueSkyConnectionInput
{
    public string? BlueSkyIdentifier { get; set; }
    public string? BlueSkyAppPassword { get; set; }
}

public sealed class BlueSkyLivePostTemplatesInput
{
    public bool PostOnStreamStart { get; set; } = true;
    public bool PostOnStreamStop { get; set; } = true;
    public string? StreamStartedTemplate { get; set; }
    public string? StreamStoppedTemplate { get; set; }
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
