namespace TwitchTools.Web.Domain;

/// <summary>
/// A Twitch account known to be a bot (e.g. Nightbot, StreamElements) that can be
/// excluded from viewer stats across all streamers. Managed in the Admin section.
/// </summary>
public sealed class KnownBot
{
    public int Id { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string? Login { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
