namespace TwitchTools.Web.Models;

public sealed class AdminKnownBotsViewModel
{
    public IReadOnlyList<KnownBotItem> KnownBots { get; init; } = [];
}

public sealed class KnownBotItem
{
    public int Id { get; init; }
    public string TwitchUserId { get; init; } = string.Empty;
    public string? Login { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedUtc { get; init; }
}

public sealed class AddKnownBotInput
{
    public string TwitchUserId { get; set; } = string.Empty;
    public string? Login { get; set; }
    public string? Notes { get; set; }
}
