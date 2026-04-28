namespace TwitchTools.Web.Domain;

public sealed class TimedChatMessage
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; }
    public DateTime? LastSentUtc { get; set; }
    public bool Enabled { get; set; } = true;

    public Streamer Streamer { get; set; } = null!;
}