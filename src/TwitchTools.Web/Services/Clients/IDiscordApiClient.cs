namespace TwitchTools.Web.Services.Clients;

public interface IDiscordApiClient
{
    Task<IReadOnlyList<DiscordGuildEvent>> GetGuildEventsAsync(string guildId, CancellationToken cancellationToken);
    Task CreateGuildEventAsync(string guildId, DiscordGuildEventPayload payload, CancellationToken cancellationToken);
    Task UpdateGuildEventAsync(string guildId, string eventId, DiscordGuildEventPayload payload, CancellationToken cancellationToken);
    Task DeleteGuildEventAsync(string guildId, string eventId, CancellationToken cancellationToken);
}

public sealed record DiscordGuildEvent(string Id, string Name, string? Description);

public sealed record DiscordGuildEventPayload(
    string Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Location);
