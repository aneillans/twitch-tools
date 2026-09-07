namespace TwitchTools.Web.Options;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";

    public string BaseUrl { get; set; } = "https://www.googleapis.com/youtube/v3";
    public string OAuthBaseUrl { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
    public string TokenInfoUri { get; set; } = "https://oauth2.googleapis.com/tokeninfo";
    public string DefaultClientId { get; set; } = string.Empty;
    public string OAuthClientSecret { get; set; } = string.Empty;
    public string OAuthRedirectUri { get; set; } = "http://localhost:8080/my-tools/connect/youtube/callback";
    public string OAuthScopes { get; set; } = "https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.force-ssl";

    /// <summary>How often to poll for a newly-live broadcast while a streamer is not currently live.</summary>
    public int NotLivePollIntervalSeconds { get; set; } = 60;

    /// <summary>Floor applied to YouTube's own reported pollingIntervalMillis for live chat polling.</summary>
    public int MinChatPollIntervalSeconds { get; set; } = 10;
}
