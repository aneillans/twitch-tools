using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    public async Task<string?> PublishLiveStatePostAsync(string streamerName, bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken)
    {
        try
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
                var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                logger.LogWarning(
                    "BlueSky post publish failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(credentials.Identifier),
                    response.StatusCode,
                    errorBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<CreateRecordResponse>(stream, cancellationToken: cancellationToken);
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
        if (string.IsNullOrWhiteSpace(credentials.Identifier) || string.IsNullOrWhiteSpace(credentials.AppPassword))
        {
            logger.LogWarning("Skipping BlueSky call because Identifier or AppPassword is missing.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "xrpc/com.atproto.server.createSession");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { identifier = credentials.Identifier, password = credentials.AppPassword }),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadResponseBodyForLogsAsync(response, cancellationToken);
                logger.LogWarning(
                    "BlueSky session creation failed for {Identifier} with {StatusCode}. Response: {ResponseBody}",
                    MaskIdentifier(credentials.Identifier),
                    response.StatusCode,
                    errorBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var session = await JsonSerializer.DeserializeAsync<CreateSessionResponse>(stream, cancellationToken: cancellationToken);

            if (session is null || string.IsNullOrWhiteSpace(session.Did) || string.IsNullOrWhiteSpace(session.AccessJwt))
            {
                logger.LogWarning("BlueSky session payload was incomplete for {Identifier}.", MaskIdentifier(credentials.Identifier));
                return null;
            }

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlueSky session creation threw for {Identifier}.", MaskIdentifier(credentials.Identifier));
            ExceptionlessClient.Default.SubmitException(ex);
            return null;
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