using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Exceptionless;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Services.Clients;

public sealed class BlueSkyApiClient(
    HttpClient httpClient,
    IOptions<BlueSkyOptions> options,
    ILogger<BlueSkyApiClient> logger) : IBlueSkyApiClient
{
    private readonly BlueSkyOptions _options = options.Value;
    private const int MaxLoggedBodyLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string?> PublishLiveStatePostAsync(string postText, BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            var session = await CreateSessionAsync(credentials, cancellationToken);
            if (session is null)
            {
                return null;
            }

            var requestBody = new
            {
                repo = session.Did,
                collection = "app.bsky.feed.post",
                record = new
                {
                    text = postText,
                    createdAt = DateTime.UtcNow.ToString("O")
                }
            };

            using var request = CreateAuthenticatedRequest(HttpMethod.Post, "xrpc/com.atproto.repo.createRecord", session.AccessJwt, requestBody);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                logger.LogWarning(
                    "BlueSky post publish failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(credentials.Identifier),
                    response.StatusCode,
                    errorBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<CreateRecordResponse>(stream, JsonOptions, cancellationToken);
            return payload?.Uri;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected BlueSky publish error for {Identifier}.", MaskIdentifier(credentials.Identifier));
            ExceptionlessClient.Default.SubmitException(ex);
            return null;
        }
    }

    public async Task UpdateProfileLiveIndicatorAsync(bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        try
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
                var errorBody = await ReadResponseBodyForLogsAsync(profileResponse, cancellationToken);
                logger.LogWarning(
                    "BlueSky profile read failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(credentials.Identifier),
                    profileResponse.StatusCode,
                    errorBody);
                return;
            }

            await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken);
            var current = await JsonSerializer.DeserializeAsync<ProfileResponse>(profileStream, JsonOptions, cancellationToken);
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
                var errorBody = await ReadResponseBodyForLogsAsync(putResponse, cancellationToken);
                logger.LogWarning(
                    "BlueSky profile update failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(credentials.Identifier),
                    putResponse.StatusCode,
                    errorBody);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected BlueSky profile update error for {Identifier}.", MaskIdentifier(credentials.Identifier));
            ExceptionlessClient.Default.SubmitException(ex);
        }
    }

    private async Task<CreateSessionResponse?> CreateSessionAsync(BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        var normalizedIdentifier = NormalizeIdentifier(credentials.Identifier);
        var normalizedAppPassword = NormalizeAppPassword(credentials.AppPassword);

        if (string.IsNullOrWhiteSpace(normalizedIdentifier) || string.IsNullOrWhiteSpace(normalizedAppPassword))
        {
            logger.LogWarning("Skipping BlueSky call because Identifier or AppPassword is missing.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "xrpc/com.atproto.server.createSession");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { identifier = normalizedIdentifier, password = normalizedAppPassword }),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                logger.LogWarning(
                    "BlueSky session creation failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(normalizedIdentifier),
                    response.StatusCode,
                    errorBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var session = await JsonSerializer.DeserializeAsync<CreateSessionResponse>(stream, JsonOptions, cancellationToken);

            if (session is null || string.IsNullOrWhiteSpace(session.Did) || string.IsNullOrWhiteSpace(session.AccessJwt))
            {
                logger.LogWarning("BlueSky session payload was incomplete for {Identifier}.", MaskIdentifier(normalizedIdentifier));
                return null;
            }

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlueSky session creation threw for {Identifier}.", MaskIdentifier(normalizedIdentifier));
            ExceptionlessClient.Default.SubmitException(ex);
            return null;
        }
    }

    public async Task SetLiveStatusAsync(bool isLive, string? streamUrl = null, int durationMinutes = 120, BlueSkyCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await CreateSessionAsync(credentials ?? new BlueSkyCredentials(string.Empty, string.Empty), cancellationToken);
            if (session is null)
            {
                return;
            }

            if (isLive)
            {
                // Create or update the live status record
                var embed = !string.IsNullOrWhiteSpace(streamUrl)
                    ? new
                    {
                        type = "app.bsky.embed.external",
                        external = new
                        {
                            uri = streamUrl,
                            title = "Watch Live Stream",
                            description = "Currently streaming on Twitch"
                        }
                    }
                    : null;

                var statusRecord = new
                {
                    status = "app.bsky.actor.status#live",
                    embed,
                    durationMinutes = Math.Min(durationMinutes, 240), // Cap at 4 hours as per atproto limits
                    createdAt = DateTime.UtcNow.ToString("O")
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "xrpc/com.atproto.repo.putRecord");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessJwt);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        repo = session.Did,
                        collection = "app.bsky.actor.status",
                        rkey = "self",
                        record = statusRecord
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                    logger.LogWarning(
                        "BlueSky live status set failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                        MaskIdentifier(credentials?.Identifier),
                        response.StatusCode,
                        errorBody);
                }
                else
                {
                    logger.LogInformation("BlueSky live status set successfully for {Identifier}", MaskIdentifier(credentials?.Identifier));
                }
            }
            else
            {
                // Delete the live status record by sending a DELETE request
                using var request = new HttpRequestMessage(HttpMethod.Post, "xrpc/com.atproto.repo.deleteRecord");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessJwt);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        repo = session.Did,
                        collection = "app.bsky.actor.status",
                        rkey = "self"
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                    logger.LogWarning(
                        "BlueSky live status clear failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                        MaskIdentifier(credentials?.Identifier),
                        response.StatusCode,
                        errorBody);
                }
                else
                {
                    logger.LogInformation("BlueSky live status cleared successfully for {Identifier}", MaskIdentifier(credentials?.Identifier));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected BlueSky live status update error for {Identifier}.", MaskIdentifier(credentials?.Identifier));
            ExceptionlessClient.Default.SubmitException(ex);
        }
    }

    public async Task<bool> TestConnectionAsync(BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            var session = await CreateSessionAsync(credentials, cancellationToken);
            var success = session is not null && !string.IsNullOrWhiteSpace(session.Did);

            logger.LogInformation(
                "BlueSky connection test for {Identifier} completed with success={Success}.",
                MaskIdentifier(credentials.Identifier),
                success);

            return success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlueSky connection test crashed for {Identifier}.", MaskIdentifier(credentials.Identifier));
            ExceptionlessClient.Default.SubmitException(ex);
            return false;
        }
    }

    private static string MaskIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "<empty>";
        }

        if (identifier.Length <= 4)
        {
            return "****";
        }

        return $"{identifier[..2]}***{identifier[^2..]}";
    }

    private static async Task<string> ReadResponseBodyForLogsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "<empty>";
        }

        return raw.Length <= MaxLoggedBodyLength
            ? raw
            : raw[..MaxLoggedBodyLength] + "...";
    }

    private static string NormalizeIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }

        var value = identifier.Trim();

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Host.Equals("bsky.app", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0].Equals("profile", StringComparison.OrdinalIgnoreCase))
                {
                    value = segments[1];
                }
            }
        }

        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        return value.Trim();
    }

    private static string NormalizeAppPassword(string? appPassword)
    {
        if (string.IsNullOrWhiteSpace(appPassword))
        {
            return string.Empty;
        }

        return string.Concat(appPassword.Where(c => !char.IsWhiteSpace(c)));
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
        [JsonPropertyName("did")]
        public string Did { get; init; } = string.Empty;

        [JsonPropertyName("accessJwt")]
        public string AccessJwt { get; init; } = string.Empty;
    }

    private sealed class CreateRecordResponse
    {
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }
    }

    private sealed class ProfileResponse
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}