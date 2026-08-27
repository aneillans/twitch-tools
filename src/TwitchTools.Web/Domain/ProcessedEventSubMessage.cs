namespace TwitchTools.Web.Domain;

/// <summary>
/// Tracks Twitch-Eventsub-Message-Id values that have already been accepted for processing so that
/// redelivered webhook notifications (Twitch retries when it does not receive a timely 2xx response)
/// are ignored instead of being processed again.
/// </summary>
public sealed class ProcessedEventSubMessage
{
    public long Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;
}
