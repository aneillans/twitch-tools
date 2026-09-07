namespace TwitchTools.Web.Models;

public sealed class ViewerStatsViewModel
{
    public IReadOnlyList<ViewerStatRow> Rows { get; init; } = [];
    public int TotalViewers { get; init; }
    public IReadOnlyList<SuggestedLinkRow> SuggestedLinks { get; init; } = [];
    public IReadOnlyList<ViewerPickerItem> UnlinkedTwitchViewers { get; init; } = [];
    public IReadOnlyList<ViewerPickerItem> UnlinkedYouTubeViewers { get; init; } = [];
}

public sealed class ViewerStatRow
{
    public string? TwitchViewerId { get; init; }
    public string? TwitchUserName { get; init; }
    public bool IsDeletedUser { get; init; }
    public int? TwitchSecondsWatched { get; init; }

    public string? YouTubeViewerId { get; init; }
    public string? YouTubeUserName { get; init; }
    public int? YouTubeSecondsWatched { get; init; }

    public long? LinkId { get; init; }

    public int TotalSecondsWatched => (TwitchSecondsWatched ?? 0) + (YouTubeSecondsWatched ?? 0);
    public TimeSpan TotalTime => TimeSpan.FromSeconds(TotalSecondsWatched);

    public bool IsLinked => TwitchViewerId is not null && YouTubeViewerId is not null;

    public string Platform => (TwitchViewerId is not null, YouTubeViewerId is not null) switch
    {
        (true, true) => "Twitch + YouTube",
        (true, false) => "Twitch",
        (false, true) => "YouTube",
        _ => "-"
    };

    /// <summary>The name shown as the row's primary label.</summary>
    public string DisplayName => TwitchUserName ?? YouTubeUserName ?? TwitchViewerId ?? YouTubeViewerId ?? "Unknown viewer";
}

public sealed class SuggestedLinkRow
{
    public string TwitchViewerId { get; init; } = string.Empty;
    public string TwitchUserName { get; init; } = string.Empty;
    public string YouTubeViewerId { get; init; } = string.Empty;
    public string YouTubeUserName { get; init; } = string.Empty;
}

public sealed class ViewerPickerItem
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
