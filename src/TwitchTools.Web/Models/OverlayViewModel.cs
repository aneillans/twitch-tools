namespace TwitchTools.Web.Models;

public sealed class OverlayViewModel
{
    public string? LastFollowerName { get; init; }
    public DateTime? LastFollowerUtc { get; init; }
    public string? LastSubscriberName { get; init; }
    public DateTime? LastSubscriberUtc { get; init; }
}