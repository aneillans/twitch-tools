using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Services.Clients;

public sealed class DiscordApiClient(
    HttpClient httpClient,
    IOptions<DiscordOptions> options,
    ILogger<DiscordApiClient> logger) : IDiscordApiClient
{
    private readonly DiscordOptions _options = options.Value;

    public async Task<IReadOnlyList<DiscordGuildEvent>> GetGuildEventsAsync(string guildId, CancellationToken cancellationToken)
    {
        if (!HasBotToken())
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"guilds/{guildId}/scheduled-events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord get guild events failed for guild {GuildId}: {StatusCode}", guildId, response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var events = await JsonSerializer.DeserializeAsync<List<DiscordScheduledEventDto>>(stream, cancellationToken: cancellationToken);
        return events?.Select(e => new DiscordGuildEvent(e.Id, e.Name, e.Description)).ToList() ?? [];
    }

    public async Task CreateGuildEventAsync(string guildId, DiscordGuildEventPayload payload, CancellationToken cancellationToken)
    {
        if (!HasBotToken())
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"guilds/{guildId}/scheduled-events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);
        request.Content = BuildBody(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord create guild event failed for guild {GuildId}: {StatusCode}", guildId, response.StatusCode);
        }
    }

    public async Task UpdateGuildEventAsync(string guildId, string eventId, DiscordGuildEventPayload payload, CancellationToken cancellationToken)
    {
        if (!HasBotToken())
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"guilds/{guildId}/scheduled-events/{eventId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);
        request.Content = BuildBody(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord update guild event failed for guild {GuildId} event {EventId}: {StatusCode}", guildId, eventId, response.StatusCode);
        }
    }

    public async Task DeleteGuildEventAsync(string guildId, string eventId, CancellationToken cancellationToken)
    {
        if (!HasBotToken())
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"guilds/{guildId}/scheduled-events/{eventId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord delete guild event failed for guild {GuildId} event {EventId}: {StatusCode}", guildId, eventId, response.StatusCode);
        }
    }

    private bool HasBotToken()
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Discord bot token is not configured; skipping guild event operation.");
            return false;
        }

        return true;
    }

    private static StringContent BuildBody(DiscordGuildEventPayload payload)
    {
        var body = new
        {
            name = payload.Name,
            description = payload.Description,
            scheduled_start_time = payload.StartTime.ToString("o"),
            scheduled_end_time = payload.EndTime.ToString("o"),
            entity_type = 3,         // EXTERNAL
            entity_metadata = new { location = payload.Location },
            privacy_level = 2        // GUILD_ONLY
        };

        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }

    private sealed class DiscordScheduledEventDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}