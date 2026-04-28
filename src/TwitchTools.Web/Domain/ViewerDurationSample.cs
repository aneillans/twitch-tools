namespace TwitchTools.Web.Domain;

public sealed class ViewerDurationSample
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string TwitchViewerId { get; set; } = string.Empty;
    public int TotalSecondsWatched { get; set; }
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}