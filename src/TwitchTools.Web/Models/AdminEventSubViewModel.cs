using TwitchTools.Web.Domain;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Models;

public sealed class AdminEventSubViewModel
{
    public EventSubDiagnosticsResult Diagnostics { get; init; } = new(false, [], 0, 0, [], null);
    public IReadOnlyList<EventSubDebugMessage> RecentPayloads { get; init; } = [];
}