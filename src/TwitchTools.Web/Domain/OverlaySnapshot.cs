namespace TwitchTools.Web.Domain;

public sealed class OverlaySnapshot
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string? LastFollowerName { get; set; }
    public DateTime? LastFollowerUtc { get; set; }
    public string? LastSubscriberName { get; set; }
    public DateTime? LastSubscriberUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}