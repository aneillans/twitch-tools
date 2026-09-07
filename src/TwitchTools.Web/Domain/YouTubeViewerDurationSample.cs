namespace TwitchTools.Web.Domain;

/// <summary>
/// YouTube analog of <see cref="ViewerDurationSample"/>. YouTube's API exposes no "who is
/// currently watching/in chat" list like Twitch's chatters endpoint - only a stream of chat
/// messages, each carrying its author - so a viewer can only be detected and timed while they are
/// actively chatting. One row exists per (streamer, viewer, broadcast); it is updated in place as
/// more messages arrive during that broadcast, and a new row is started (carrying the running
/// total forward via <see cref="SessionBaseSeconds"/>) when the same viewer is next seen under a
/// different <see cref="VideoId"/>. As with <see cref="ViewerDurationSample"/>, a viewer's latest
/// row (by <see cref="CapturedUtc"/>) holds their current all-time cumulative total.
/// </summary>
public sealed class YouTubeViewerDurationSample
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string YouTubeViewerId { get; set; } = string.Empty;
    public string? YouTubeViewerDisplayName { get; set; }
    public string VideoId { get; set; } = string.Empty;
    public int SessionBaseSeconds { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int TotalSecondsWatched { get; set; }
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}
