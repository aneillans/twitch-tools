namespace TwitchTools.Web.Domain;

public sealed class LiveNotificationEvent
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public bool IsLive { get; set; }
    public string? BlueSkyPostUri { get; set; }
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}