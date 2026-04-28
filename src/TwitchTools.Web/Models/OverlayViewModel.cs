namespace TwitchTools.Web.Models;

public sealed class OverlayWidgetViewModel
{
    public string Title { get; init; } = string.Empty;
    public string EmptyMessage { get; init; } = string.Empty;
    public string? DisplayValue { get; init; }
    public DateTime? EventUtc { get; init; }
}