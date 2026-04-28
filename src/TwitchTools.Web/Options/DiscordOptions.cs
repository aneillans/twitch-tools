namespace TwitchTools.Web.Options;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string BaseUrl { get; set; } = "https://discord.com/api/v10";
    public string BotToken { get; set; } = string.Empty;
    public string BotClientId { get; set; } = string.Empty;
    // View Channels (1024) + Manage Events (8589934592)
    public string InvitePermissions { get; set; } = "8589935616";
    public string InviteScopes { get; set; } = "bot";
}