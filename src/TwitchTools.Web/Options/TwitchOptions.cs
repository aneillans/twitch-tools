namespace TwitchTools.Web.Options;

public sealed class TwitchOptions
{
    public const string SectionName = "Twitch";

    public string BaseUrl { get; set; } = "https://api.twitch.tv";
    public string OAuthBaseUrl { get; set; } = "https://id.twitch.tv";
    public string DefaultClientId { get; set; } = string.Empty;
    public string OAuthClientSecret { get; set; } = string.Empty;
    public string OAuthRedirectUri { get; set; } = "http://localhost:8080/my-tools/connect/twitch/callback";
    public string OAuthScopes { get; set; } = "moderator:read:chatters moderator:read:followers user:read:chat user:write:chat user:bot channel:bot channel:read:subscriptions";
    public string EventSubCallbackUrl { get; set; } = string.Empty;
    public string EventSubSecret { get; set; } = string.Empty;
    public string? DefaultBotUserId { get; set; }
}