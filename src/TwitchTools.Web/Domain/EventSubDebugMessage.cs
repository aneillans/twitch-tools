namespace TwitchTools.Web.Domain;

public sealed class EventSubDebugMessage
{
    public long Id { get; set; }
    public Guid? StreamerId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string? SubscriptionType { get; set; }
    public string? MessageId { get; set; }
    public string? BroadcasterUserId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

    public Streamer? Streamer { get; set; }
}