namespace TwitchTools.Web.Models;

public sealed class ViewerStatsViewModel
{
    public IReadOnlyList<ViewerStatRow> Rows { get; init; } = [];
    public int TotalViewers { get; init; }
}

public sealed class ViewerStatRow
{
    public string TwitchViewerId { get; init; } = string.Empty;
    public string? TwitchUserName { get; init; }
    public bool IsDeletedUser { get; init; }
    public int TotalSecondsWatched { get; init; }
    public TimeSpan TotalTime => TimeSpan.FromSeconds(TotalSecondsWatched);
}
