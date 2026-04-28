namespace TwitchTools.Web.Domain;

public sealed class DiscordGuildSync
{
    public long Id { get; set; }
    public Guid StreamerId { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public DateTime LastSyncedUtc { get; set; }

    public Streamer Streamer { get; set; } = null!;
}