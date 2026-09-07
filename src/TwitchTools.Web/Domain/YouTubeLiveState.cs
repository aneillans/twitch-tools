namespace TwitchTools.Web.Domain;

/// <summary>
/// Mutable, derived YouTube live/chat-polling state for a streamer. Kept off <see cref="Streamer"/>
/// (same reasoning as <see cref="OverlaySnapshot"/>) since it churns on every poll cycle while a
/// stream is live, whereas connection settings on <see cref="Streamer"/> only change when the
/// streamer edits them.
/// </summary>
public sealed class YouTubeLiveState
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public bool IsLive { get; set; }
    public string? VideoId { get; set; }
    public string? LiveChatId { get; set; }
    public string? NextPageToken { get; set; }
    public DateTime? LastPolledUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}
