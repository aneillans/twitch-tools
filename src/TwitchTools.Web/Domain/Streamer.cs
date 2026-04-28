namespace TwitchTools.Web.Domain;

public sealed class Streamer
{
    public Guid Id { get; set; }
    public string OwnerSubject { get; set; } = string.Empty;
    public string? OwnerEmail { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string TwitchStreamerAccessToken { get; set; } = string.Empty;
    public string? TwitchStreamerRefreshToken { get; set; }
    public string? TwitchClientId { get; set; }
    public string? TwitchBotUserId { get; set; }
    public string? TwitchBotAccessToken { get; set; }
    public string? TwitchBotRefreshToken { get; set; }
    public string? TwitchModeratorUserId { get; set; }
    public string? BlueSkyIdentifier { get; set; }
    public string? BlueSkyAppPassword { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string OverlayToken { get; set; } = string.Empty;
    public string FollowerOverlayToken { get; set; } = string.Empty;
    public string SubscriberOverlayToken { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<LiveNotificationEvent> LiveNotificationEvents { get; set; } = new List<LiveNotificationEvent>();
    public ICollection<TimedChatMessage> TimedChatMessages { get; set; } = new List<TimedChatMessage>();
    public ICollection<ViewerDurationSample> ViewerDurationSamples { get; set; } = new List<ViewerDurationSample>();
    public OverlaySnapshot? OverlaySnapshot { get; set; }
}