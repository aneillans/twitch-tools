using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Exceptionless;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Models;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class MyToolsController(
    AppDbContext dbContext,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<DiscordOptions> discordOptions,
    ITwitchApiClient twitchApiClient,
    IHttpClientFactory httpClientFactory,
    IDiscordScheduleSyncService discordSyncService,
    IBlueSkyApiClient blueSkyApiClient,
    ILogger<MyToolsController> logger) : Controller
{
    private const string TwitchOAuthStateCookie = "twitch_oauth_state";
    private const string TwitchOAuthModeCookie = "twitch_oauth_mode";
    private const string DefaultBlueSkyStartedTemplate = "{streamer} is now live on Twitch.";
    private const string DefaultBlueSkyStoppedTemplate = "{streamer} has ended the stream.";
    private static readonly JsonSerializerOptions TwitchJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet("/my-tools")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var streamer = await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

        if (streamer is null)
        {
            ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
            return View(new MyToolsViewModel());
        }

        var model = new MyToolsViewModel
        {
            HasProfile = true,
            Twitch = new TwitchConnectionInput
            {
                DisplayName = streamer.DisplayName,
                TwitchUserId = streamer.TwitchUserId,
                TwitchBotUserId = streamer.TwitchBotUserId,
                StreamerTokenStatus = await BuildTokenStatusAsync(streamer.TwitchStreamerAccessToken, streamer.TwitchUserId, cancellationToken),
                BotTokenStatus = await BuildTokenStatusAsync(streamer.TwitchBotAccessToken, streamer.TwitchBotUserId, cancellationToken),
                TwitchStreamerAccessToken = streamer.TwitchStreamerAccessToken,
                TwitchStreamerRefreshToken = streamer.TwitchStreamerRefreshToken,
                TwitchClientId = streamer.TwitchClientId,
                TwitchBotAccessToken = streamer.TwitchBotAccessToken,
                TwitchBotRefreshToken = streamer.TwitchBotRefreshToken
            },
            BlueSky = new BlueSkyConnectionInput
            {
                BlueSkyIdentifier = streamer.BlueSkyIdentifier,
                BlueSkyAppPassword = streamer.BlueSkyAppPassword
            }
        };

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpGet("/my-tools/live-automation")]
    public async Task<IActionResult> LiveAutomation(CancellationToken cancellationToken)
    {
        ViewData["DiscordInviteUrl"] = BuildDiscordInviteUrl(discordOptions.Value);

        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var streamer = await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

        var model = new LiveAutomationViewModel();
        if (streamer is not null)
        {
            var discordSyncs = await dbContext.DiscordGuildSyncs
                .AsNoTracking()
                .Where(x => x.StreamerId == streamer.Id)
                .OrderBy(x => x.Id)
                .Select(x => new DiscordSyncItem
                {
                    Id = x.Id,
                    GuildId = x.GuildId,
                    LastSyncedUtc = x.LastSyncedUtc
                })
                .ToListAsync(cancellationToken);

            model = new LiveAutomationViewModel
            {
                DiscordSyncs = discordSyncs,
                IsBlueSkyConfigured = !string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier)
                    && !string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword),
                BlueSkyTemplates = new BlueSkyLivePostTemplatesInput
                {
                    PostOnStreamStart = streamer.BlueSkyPostOnStreamStart,
                    PostOnStreamStop = streamer.BlueSkyPostOnStreamStop,
                    StreamStartedTemplate = string.IsNullOrWhiteSpace(streamer.BlueSkyStreamStartedTemplate)
                        ? DefaultBlueSkyStartedTemplate
                        : streamer.BlueSkyStreamStartedTemplate,
                    StreamStoppedTemplate = string.IsNullOrWhiteSpace(streamer.BlueSkyStreamStoppedTemplate)
                        ? DefaultBlueSkyStoppedTemplate
                        : streamer.BlueSkyStreamStoppedTemplate
                }
            };
        }

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpGet("/my-tools/connect/twitch")]
    public IActionResult ConnectTwitch()
    {
        return BeginTwitchOAuth("streamer");
    }

    [HttpGet("/my-tools/connect/twitch-bot")]
    public IActionResult ConnectTwitchBot()
    {
        return BeginTwitchOAuth("bot");
    }

    private IActionResult BeginTwitchOAuth(string mode)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var options = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(options.DefaultClientId)
            || string.IsNullOrWhiteSpace(options.OAuthClientSecret)
            || string.IsNullOrWhiteSpace(options.OAuthRedirectUri))
        {
            TempData["StatusMessage"] = "Twitch OAuth is not fully configured on the server.";
            return RedirectToAction(nameof(Index));
        }

        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(TwitchOAuthStateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        Response.Cookies.Append(TwitchOAuthModeCookie, mode, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var authUrl =
            $"{options.OAuthBaseUrl.TrimEnd('/')}/oauth2/authorize" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(options.DefaultClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(options.OAuthRedirectUri)}" +
            $"&scope={Uri.EscapeDataString(options.OAuthScopes)}" +
            $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    [HttpGet("/my-tools/connect/twitch/callback")]
    public async Task<IActionResult> TwitchCallback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            TempData["StatusMessage"] = $"Twitch connection failed: {error}";
            return RedirectToAction(nameof(Index));
        }

        var expectedState = Request.Cookies[TwitchOAuthStateCookie];
        var oauthMode = Request.Cookies[TwitchOAuthModeCookie];
        Response.Cookies.Delete(TwitchOAuthStateCookie);
        Response.Cookies.Delete(TwitchOAuthModeCookie);
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(expectedState) || !string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            TempData["StatusMessage"] = "Twitch connection failed due to invalid OAuth state.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["StatusMessage"] = "Twitch connection failed because no authorization code was returned.";
            return RedirectToAction(nameof(Index));
        }

        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        try
        {
            var options = twitchOptions.Value;
            var oauthClient = httpClientFactory.CreateClient();

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.OAuthBaseUrl.TrimEnd('/')}/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.DefaultClientId,
                    ["client_secret"] = options.OAuthClientSecret,
                    ["code"] = code,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = options.OAuthRedirectUri
                })
            };

            using var tokenResponse = await oauthClient.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                TempData["StatusMessage"] = "Twitch token exchange failed.";
                return RedirectToAction(nameof(Index));
            }

            await using var tokenStream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
            var tokenPayload = await JsonSerializer.DeserializeAsync<TwitchTokenResponse>(
                tokenStream,
                TwitchJsonOptions,
                cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(tokenPayload?.AccessToken))
            {
                TempData["StatusMessage"] = "Twitch token payload did not include an access token.";
                return RedirectToAction(nameof(Index));
            }

            var apiClient = httpClientFactory.CreateClient();
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, $"{options.BaseUrl.TrimEnd('/')}/helix/users");
            userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenPayload.AccessToken);
            userRequest.Headers.Add("Client-Id", options.DefaultClientId);

            using var userResponse = await apiClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                TempData["StatusMessage"] = "Unable to read Twitch user details with the granted token.";
                return RedirectToAction(nameof(Index));
            }

            await using var userStream = await userResponse.Content.ReadAsStreamAsync(cancellationToken);
            var userPayload = await JsonSerializer.DeserializeAsync<TwitchUserEnvelope>(
                userStream,
                TwitchJsonOptions,
                cancellationToken: cancellationToken);
            var user = userPayload?.Data.FirstOrDefault();
            if (user is null || string.IsNullOrWhiteSpace(user.Id))
            {
                TempData["StatusMessage"] = "Twitch user details were empty.";
                return RedirectToAction(nameof(Index));
            }

            var streamer = await GetOrCreateOwnedStreamerAsync(ownerSubject, cancellationToken);
            if (string.Equals(oauthMode, "bot", StringComparison.OrdinalIgnoreCase))
            {
                streamer.TwitchBotUserId = user.Id;
                streamer.TwitchBotAccessToken = tokenPayload.AccessToken;
                streamer.TwitchBotRefreshToken = tokenPayload.RefreshToken;
                streamer.TwitchClientId ??= options.DefaultClientId;

                await dbContext.SaveChangesAsync(cancellationToken);
                TempData["StatusMessage"] = "Twitch bot account connected successfully.";
                return RedirectToAction(nameof(Index));
            }

            streamer.DisplayName = !string.IsNullOrWhiteSpace(streamer.DisplayName) ? streamer.DisplayName : (user.DisplayName ?? user.Login ?? "Streamer");
            streamer.TwitchUserId = user.Id;
            streamer.TwitchStreamerAccessToken = tokenPayload.AccessToken;
            streamer.TwitchStreamerRefreshToken = tokenPayload.RefreshToken;
            streamer.TwitchClientId = options.DefaultClientId;

            await dbContext.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = "Twitch connection updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Twitch OAuth callback failed.");
            ExceptionlessClient.Default.SubmitException(ex);
            TempData["StatusMessage"] = "Twitch connection failed unexpectedly.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/my-tools/twitch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTwitch([Bind(Prefix = nameof(MyToolsViewModel.Twitch))] TwitchConnectionInput input, CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var streamer = await GetOrCreateOwnedStreamerAsync(ownerSubject, cancellationToken);

        streamer.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? streamer.DisplayName : input.DisplayName.Trim();
        streamer.TwitchUserId = string.IsNullOrWhiteSpace(input.TwitchUserId) ? streamer.TwitchUserId : input.TwitchUserId.Trim();
        streamer.TwitchStreamerAccessToken = string.IsNullOrWhiteSpace(input.TwitchStreamerAccessToken) ? streamer.TwitchStreamerAccessToken : input.TwitchStreamerAccessToken.Trim();
        streamer.TwitchStreamerRefreshToken = string.IsNullOrWhiteSpace(input.TwitchStreamerRefreshToken) ? streamer.TwitchStreamerRefreshToken : input.TwitchStreamerRefreshToken.Trim();
        streamer.TwitchClientId = string.IsNullOrWhiteSpace(input.TwitchClientId) ? streamer.TwitchClientId : input.TwitchClientId.Trim();
        streamer.TwitchBotAccessToken = TrimToNull(input.TwitchBotAccessToken);
        streamer.TwitchBotRefreshToken = TrimToNull(input.TwitchBotRefreshToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Twitch settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/my-tools/bluesky/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestBlueSky(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null || string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier) || string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword))
        {
            TempData["StatusMessage"] = "Save your BlueSky identifier and app password before testing the connection.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var credentials = new BlueSkyCredentials(streamer.BlueSkyIdentifier, streamer.BlueSkyAppPassword);
            var ok = await blueSkyApiClient.TestConnectionAsync(credentials, cancellationToken);
            TempData["StatusMessage"] = ok
                ? "BlueSky connection successful."
                : "BlueSky connection failed. Check your identifier and app password.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlueSky connection test failed for streamer {StreamerId}.", streamer.Id);
            ExceptionlessClient.Default.SubmitException(ex);
            TempData["StatusMessage"] = "BlueSky connection failed unexpectedly. The error has been captured for review.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/my-tools/bluesky")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBlueSky([Bind(Prefix = nameof(MyToolsViewModel.BlueSky))] BlueSkyConnectionInput input, CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return Challenge();
        }

        var streamer = await GetOrCreateOwnedStreamerAsync(ownerSubject, cancellationToken);
        streamer.BlueSkyIdentifier = TrimToNull(input.BlueSkyIdentifier);
        streamer.BlueSkyAppPassword = TrimToNull(input.BlueSkyAppPassword);

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "BlueSky settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/my-tools/bluesky/templates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBlueSkyTemplates([Bind(Prefix = nameof(LiveAutomationViewModel.BlueSkyTemplates))] BlueSkyLivePostTemplatesInput input, CancellationToken cancellationToken)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            return RedirectToAction(nameof(LiveAutomation));
        }

        var isBlueSkyConfigured = !string.IsNullOrWhiteSpace(streamer.BlueSkyIdentifier)
            && !string.IsNullOrWhiteSpace(streamer.BlueSkyAppPassword);
        if (!isBlueSkyConfigured)
        {
            TempData["StatusMessage"] = "Configure BlueSky credentials on My Tools before editing stream announcement text.";
            return RedirectToAction(nameof(LiveAutomation));
        }

        streamer.BlueSkyStreamStartedTemplate = TrimToNull(input.StreamStartedTemplate);
        streamer.BlueSkyStreamStoppedTemplate = TrimToNull(input.StreamStoppedTemplate);
        streamer.BlueSkyPostOnStreamStart = input.PostOnStreamStart;
        streamer.BlueSkyPostOnStreamStop = input.PostOnStreamStop;

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "BlueSky stream announcement templates saved.";
        return RedirectToAction(nameof(LiveAutomation));
    }

    [HttpPost("/my-tools/discord-syncs")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDiscordSync(AddDiscordSyncInput input, CancellationToken cancellationToken)
    {
        var guildId = input.GuildId?.Trim();

        if (string.IsNullOrWhiteSpace(guildId) || !guildId.All(char.IsDigit))
        {
            TempData["StatusMessage"] = "Please enter a valid Discord Guild (Server) ID.";
            return RedirectToAction(nameof(LiveAutomation));
        }

        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            return RedirectToAction(nameof(LiveAutomation));
        }

        dbContext.DiscordGuildSyncs.Add(new DiscordGuildSync
        {
            StreamerId = streamer.Id,
            GuildId = guildId,
            LastSyncedUtc = DateTime.UnixEpoch
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Discord server added. Scheduled events will sync on the next cycle.";
        return RedirectToAction(nameof(LiveAutomation));
    }

    [HttpPost("/my-tools/discord-syncs/{id:long}/sync")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncDiscordNow(long id, CancellationToken cancellationToken)
    {
        var sync = await dbContext.DiscordGuildSyncs
            .Include(x => x.Streamer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (sync is null || !IsOwner(sync.Streamer.OwnerSubject))
        {
            return RedirectToAction(nameof(LiveAutomation));
        }

        try
        {
            await discordSyncService.SyncScheduleAsync(sync.Streamer, cancellationToken);
            TempData["StatusMessage"] = $"Discord schedule synced for server {sync.GuildId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual Discord sync failed for guild {GuildId}.", sync.GuildId);
            ExceptionlessClient.Default.SubmitException(ex);
            TempData["StatusMessage"] = $"Discord sync failed for server {sync.GuildId}. Check logs for details.";
        }

        return RedirectToAction(nameof(LiveAutomation));
    }

    [HttpPost("/my-tools/discord-syncs/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDiscordSync(long id, CancellationToken cancellationToken)
    {
        var sync = await dbContext.DiscordGuildSyncs
            .Include(x => x.Streamer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (sync is null || !IsOwner(sync.Streamer.OwnerSubject))
        {
            return RedirectToAction(nameof(LiveAutomation));
        }

        dbContext.DiscordGuildSyncs.Remove(sync);
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"Discord server {sync.GuildId} removed.";
        return RedirectToAction(nameof(LiveAutomation));
    }

    private async Task<Streamer?> GetOwnedStreamerAsync(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return null;
        }

        return await dbContext.Streamers.FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
    }

    private async Task<Streamer> GetOrCreateOwnedStreamerAsync(string ownerSubject, CancellationToken cancellationToken)
    {
        var ownerEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        var streamer = await dbContext.Streamers.FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
        if (streamer is not null)
        {
            streamer.OwnerEmail = ownerEmail;
            streamer.FollowerOverlayToken = EnsureToken(streamer.FollowerOverlayToken, streamer.OverlayToken);
            streamer.SubscriberOverlayToken = EnsureToken(streamer.SubscriberOverlayToken);
            return streamer;
        }

        streamer = new Streamer
        {
            Id = Guid.NewGuid(),
            OwnerSubject = ownerSubject,
            OwnerEmail = ownerEmail,
            OverlayToken = Guid.NewGuid().ToString("N"),
            FollowerOverlayToken = Guid.NewGuid().ToString("N"),
            SubscriberOverlayToken = Guid.NewGuid().ToString("N")
        };

        dbContext.Streamers.Add(streamer);
        return streamer;
    }

    private string? GetOwnerSubject()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }

    private bool IsOwner(string ownerSubject)
    {
        var current = GetOwnerSubject();
        return !string.IsNullOrWhiteSpace(current) && string.Equals(current, ownerSubject, StringComparison.Ordinal);
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private async Task<TwitchTokenStatusViewModel> BuildTokenStatusAsync(string? accessToken, string? expectedUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return TwitchTokenStatusViewModel.Missing();
        }

        var result = await twitchApiClient.ValidateAccessTokenAsync(accessToken, cancellationToken);
        if (!result.IsValid)
        {
            return new TwitchTokenStatusViewModel
            {
                State = "Invalid",
                Details = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Token was rejected by Twitch." : result.ErrorMessage,
                TextClass = "text-danger"
            };
        }

        if (!string.IsNullOrWhiteSpace(expectedUserId)
            && !string.IsNullOrWhiteSpace(result.UserId)
            && !string.Equals(expectedUserId, result.UserId, StringComparison.Ordinal))
        {
            return new TwitchTokenStatusViewModel
            {
                State = "Mismatch",
                Details = $"Token belongs to Twitch user {result.Login ?? result.UserId}, expected {expectedUserId}.",
                TextClass = "text-warning"
            };
        }

        if (result.ExpiresInSeconds is <= 3600)
        {
            return new TwitchTokenStatusViewModel
            {
                State = "Expiring soon",
                Details = $"Token is valid but expires in about {Math.Max(0, result.ExpiresInSeconds ?? 0) / 60} minutes.",
                TextClass = "text-warning"
            };
        }

        return new TwitchTokenStatusViewModel
        {
            State = "Valid",
            Details = string.IsNullOrWhiteSpace(result.Login)
                ? "Token validated successfully."
                : $"Valid for Twitch user {result.Login} ({result.UserId}).",
            TextClass = "text-success"
        };
    }

    private static string? BuildDiscordInviteUrl(DiscordOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BotClientId))
        {
            return null;
        }

        return "https://discord.com/oauth2/authorize"
            + "?client_id=" + Uri.EscapeDataString(options.BotClientId)
            + "&permissions=" + Uri.EscapeDataString(options.InvitePermissions)
            + "&scope=" + Uri.EscapeDataString(options.InviteScopes);
    }

    private static string EnsureToken(string? token, string? fallbackToken = null)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (!string.IsNullOrWhiteSpace(fallbackToken))
        {
            return fallbackToken;
        }

        return Guid.NewGuid().ToString("N");
    }

    private sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
    }

    private sealed class TwitchUserEnvelope
    {
        [JsonPropertyName("data")]
        public List<TwitchUser> Data { get; init; } = [];
    }

    private sealed class TwitchUser
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }
    }
}
