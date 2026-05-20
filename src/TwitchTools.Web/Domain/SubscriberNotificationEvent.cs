namespace TwitchTools.Web.Domain;

public sealed class SubscriberNotificationEvent
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string? TwitchUserLogin { get; set; }
    public string? TwitchUserName { get; set; }
    public bool IsGift { get; set; }
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}