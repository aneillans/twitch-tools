using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Services.Clients;

public sealed class BlueSkyApiClient(
    HttpClient httpClient,
    IOptions<BlueSkyOptions> options,
    ILogger<BlueSkyApiClient> logger) : IBlueSkyApiClient
{
    private readonly BlueSkyOptions _options = options.Value;

    public async Task<string?> PublishLiveStatePostAsync(string streamerName, bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        var session = await CreateSessionAsync(credentials, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var text = isLive
            ? $"{streamerName} is now live on Twitch."
            : $"{streamerName} has ended the stream.";

        var requestBody = new
        {
            repo = session.Did,
            collection = "app.bsky.feed.post",
            record = new
            {
                text,
                createdAt = DateTime.UtcNow.ToString("O")
            }
        };

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "xrpc/com.atproto.repo.createRecord", session.AccessJwt, requestBody);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("BlueSky post publish failed with {StatusCode}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<CreateRecordResponse>(stream, cancellationToken: cancellationToken);
        return payload?.Uri;
    }

    public async Task UpdateProfileLiveIndicatorAsync(bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        var session = await CreateSessionAsync(credentials, cancellationToken);
        if (session is null)
        {
            return;
        }

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, $"xrpc/app.bsky.actor.getProfile?actor={Uri.EscapeDataString(session.Did)}");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessJwt);

        using var profileResponse = await httpClient.SendAsync(profileRequest, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("BlueSky profile read failed with {StatusCode}", profileResponse.StatusCode);
            return;
        }

        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken);
        var current = await JsonSerializer.DeserializeAsync<ProfileResponse>(profileStream, cancellationToken: cancellationToken);
        var description = current?.Description ?? string.Empty;
        var prefix = _options.LiveProfilePrefix;

        if (isLive && !description.StartsWith(prefix, StringComparison.Ordinal))
        {
            description = prefix + description;
        }

        if (!isLive && description.StartsWith(prefix, StringComparison.Ordinal))
        {
            description = description[prefix.Length..].TrimStart();
        }

        using var putProfile = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "xrpc/app.bsky.actor.putProfile",
            session.AccessJwt,
            new
            {
                displayName = current?.DisplayName,
                description
            });

        using var putResponse = await httpClient.SendAsync(putProfile, cancellationToken);
        if (!putResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("BlueSky profile update failed with {StatusCode}", putResponse.StatusCode);
        }
    }

    private async Task<CreateSessionResponse?> CreateSessionAsync(BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.Identifier) || string.IsNullOrWhiteSpace(credentials.AppPassword))
        {
            logger.LogWarning("Skipping BlueSky call because Identifier or AppPassword is missing.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "xrpc/com.atproto.server.createSession");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { identifier = credentials.Identifier, password = credentials.AppPassword }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("BlueSky session creation failed with {StatusCode}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<CreateSessionResponse>(stream, cancellationToken: cancellationToken);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativePath, string jwt, object payload)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private sealed class CreateSessionResponse
    {
        public string Did { get; init; } = string.Empty;
        public string AccessJwt { get; init; } = string.Empty;
    }

    private sealed class CreateRecordResponse
    {
        public string? Uri { get; init; }
    }

    private sealed class ProfileResponse
    {
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
    }
}