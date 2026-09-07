namespace TwitchTools.Web.Domain;

/// <summary>
/// A streamer-curated link asserting that a Twitch viewer and a YouTube viewer are the same real
/// person. There is no reliable automatic signal for this (usernames rarely match, and this app
/// has no viewer-facing login to prove it), so links are created manually - see
/// ViewerStatsController's "link"/"unlink" actions and its display-name-based suggestions. Each
/// side can only appear in one link per streamer (enforced via unique indexes in AppDbContext).
/// </summary>
public sealed class ViewerIdentityLink
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string TwitchViewerId { get; set; } = string.Empty;
    public string YouTubeViewerId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Streamer Streamer { get; set; } = null!;
}
