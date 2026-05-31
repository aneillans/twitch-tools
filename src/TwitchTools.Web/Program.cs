using Exceptionless;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using TwitchTools.Web;
using TwitchTools.Web.Background;
using TwitchTools.Web.Data;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Clients;
using TwitchTools.Web.Services.Security;
using Neillans.TemplateKit.Auth;
using Neillans.TemplateKit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));
builder.Services.Configure<TwitchOptions>(builder.Configuration.GetSection(TwitchOptions.SectionName));
builder.Services.Configure<BlueSkyOptions>(builder.Configuration.GetSection(BlueSkyOptions.SectionName));
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection(EncryptionOptions.SectionName));
builder.Services.Configure<FeatureFlagsOptions>(builder.Configuration.GetSection(FeatureFlagsOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;

    // Accept forwarded headers from container/proxy networks.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddTemplateOidcAuthentication(builder.Configuration, options =>
{
    var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
        ?? throw new InvalidOperationException("Keycloak configuration is missing.");

    options.Authority = keycloak.Authority;
    if (!string.IsNullOrWhiteSpace(keycloak.MetadataAddress))
    {
        options.MetadataAddress = keycloak.MetadataAddress;
    }

    options.ClientId = keycloak.ClientId;
    options.ClientSecret = keycloak.ClientSecret;
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.CallbackPath = keycloak.CallbackPath;

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    options.TokenValidationParameters.RoleClaimType = "roles";

    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = context =>
        {
            if (context.Principal?.Identity is not ClaimsIdentity identity)
            {
                return Task.CompletedTask;
            }

            AddRoleClaims(identity, "realm_access", "roles");
            AddResourceRoleClaims(identity, context.Options.ClientId ?? string.Empty);
            AddGroupClaimsAsRoles(identity, "groups");

            // Keycloak commonly emits role claims in the access token; ensure they are available for policies.
            AddRolesFromJwt(identity, context.TokenEndpointResponse?.AccessToken, context.Options.ClientId ?? string.Empty);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
});

// builder.Services.AddDbContext<AppDbContext>(options =>
//     options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddTemplateDbContext<AppDbContext>(builder.Configuration, options =>
{
    // Keep any project-specific EF options here (if needed).
});

builder.Services.AddExceptionless(builder.Configuration);
builder.Services.AddSingleton<IDataEncryptionService, AesDataEncryptionService>();

builder.Services.AddHttpClient<ITwitchApiClient, TwitchApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwitchOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddHttpClient<IBlueSkyApiClient, BlueSkyApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlueSkyOptions>>().Value;
    client.BaseAddress = new Uri(options.ServiceUrl.TrimEnd('/') + "/");
});

builder.Services.AddHttpClient<IDiscordApiClient, DiscordApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscordOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddScoped<IBlueSkyService, BlueSkyService>();
builder.Services.AddScoped<IDiscordScheduleSyncService, DiscordScheduleSyncService>();
builder.Services.AddScoped<ITimedChatMessageService, TimedChatMessageService>();
builder.Services.AddScoped<IViewerMonitoringService, ViewerMonitoringService>();
builder.Services.AddScoped<ITwitchEventSubService, TwitchEventSubService>();
builder.Services.AddScoped<IOverlayService, OverlayService>();
builder.Services.AddSingleton<IOverlayEventBroker, OverlayEventBroker>();

builder.Services.AddHostedService<DiscordScheduleRefreshBackgroundService>();
builder.Services.AddHostedService<TimedChatMessageBackgroundService>();
builder.Services.AddHostedService<TwitchTokenRefreshBackgroundService>();
builder.Services.AddHostedService<ViewerMonitoringBackgroundService>();
builder.Services.AddHostedService<TwitchEventSubBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseExceptionless();

var exceptionlessClient = app.Services.GetRequiredService<ExceptionlessClient>();
var exceptionlessConfig = exceptionlessClient.Configuration;
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
ConfigureGlobalExceptionForwarding(exceptionlessClient, startupLogger);

var exceptionlessStoragePath =
    Environment.GetEnvironmentVariable("EXCEPTIONLESS_STORAGE_PATH")
    ?? Path.Combine(Path.GetTempPath(), "exceptionless");

Directory.CreateDirectory(exceptionlessStoragePath);
exceptionlessConfig.UseFolderStorage(exceptionlessStoragePath);
exceptionlessConfig.UseTraceLogger(Exceptionless.Logging.LogLevel.Trace);
exceptionlessConfig.SetDefaultMinLogLevel(Exceptionless.Logging.LogLevel.Trace);

var configuredFeatureFlags = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeatureFlagsOptions>>().Value;
StartupLog.LogFeatureFlagEnableEventSubIngressLogging(startupLogger, configuredFeatureFlags.EnableEventSubIngressLogging);

if (startupLogger.IsEnabled(LogLevel.Information))
{
    StartupLog.LogExceptionlessServerUrl(startupLogger, exceptionlessConfig.ServerUrl);
    var exceptionlessApiKeyConfigured = !string.IsNullOrWhiteSpace(exceptionlessConfig.ApiKey);
    StartupLog.LogExceptionlessApiKeyConfigured(startupLogger, exceptionlessApiKeyConfigured);
    StartupLog.LogExceptionlessEnabled(startupLogger, exceptionlessConfig.IsValid);
    var storageImpl = exceptionlessConfig.Resolver.Resolve(typeof(Exceptionless.Storage.IObjectStorage));
    StartupLog.LogExceptionlessStorageImplementation(startupLogger, storageImpl?.GetType().FullName ?? "<unknown>");
    StartupLog.LogExceptionlessLocalStoragePath(startupLogger, exceptionlessStoragePath);

    var configuredTwitchOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwitchOptions>>().Value;
    StartupLog.LogConfiguredTwitchEventSubCallbackUrl(startupLogger, configuredTwitchOptions.EventSubCallbackUrl);
}

if (!Directory.Exists(exceptionlessStoragePath))
{
    StartupLog.LogExceptionlessLocalStorageDirectoryMissing(startupLogger, exceptionlessStoragePath);
}

app.UseForwardedHeaders();

if (configuredFeatureFlags.EnableEventSubIngressLogging)
{
    var eventSubIngressLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EventSubIngress");
    app.Use(async (context, next) =>
    {
        var isEventSubCandidatePath = context.Request.Path.StartsWithSegments("/eventsub", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/my-tools/connect/twitch/eventsub/callback", StringComparison.OrdinalIgnoreCase);

        if (isEventSubCandidatePath)
        {
            var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
            var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString();

            StartupLog.LogEventSubIngressCandidateReceived(
                eventSubIngressLogger,
                context.Request.Method,
                context.Request.Path.Value,
                context.Request.Scheme,
                context.Request.Host.Value,
                forwardedProto,
                forwardedHost);
        }

        await next().ConfigureAwait(false);

        if (isEventSubCandidatePath)
        {
            StartupLog.LogEventSubIngressCandidateCompleted(
                eventSubIngressLogger,
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode);
        }
    });
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/eventsub/twitch", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseHttpsRedirection());
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/account/login", async context =>
{
    await context.ChallengeAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" }).ConfigureAwait(false);
});

app.MapGet("/account/logout", async context =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    await context.SignOutAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" }).ConfigureAwait(false);
});

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

static void AddRoleClaims(ClaimsIdentity identity, string parentClaimType, string rolesPropertyName)
{
    var parentClaim = identity.FindFirst(parentClaimType)?.Value;
    if (string.IsNullOrWhiteSpace(parentClaim))
    {
        return;
    }

    using var doc = JsonDocument.Parse(parentClaim);
    if (!doc.RootElement.TryGetProperty(rolesPropertyName, out var roles) || roles.ValueKind != JsonValueKind.Array)
    {
        return;
    }

    foreach (var role in roles.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
    {
        AddRoleClaim(identity, role!);
    }
}

static void AddResourceRoleClaims(ClaimsIdentity identity, string clientId)
{
    if (string.IsNullOrWhiteSpace(clientId))
    {
        return;
    }

    var resourceAccess = identity.FindFirst("resource_access")?.Value;
    if (string.IsNullOrWhiteSpace(resourceAccess))
    {
        return;
    }

    using var doc = JsonDocument.Parse(resourceAccess);
    if (!doc.RootElement.TryGetProperty(clientId, out var clientSection))
    {
        return;
    }

    if (!clientSection.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
    {
        return;
    }

    foreach (var role in roles.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
    {
        AddRoleClaim(identity, role!);
    }
}

static void AddRolesFromJwt(ClaimsIdentity identity, string? jwt, string clientId)
{
    if (string.IsNullOrWhiteSpace(jwt))
    {
        return;
    }

    var parts = jwt.Split('.');
    if (parts.Length < 2)
    {
        return;
    }

    try
    {
        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        var bytes = Convert.FromBase64String(payload);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        if (root.TryGetProperty("realm_access", out var realmAccess)
            && realmAccess.TryGetProperty("roles", out var realmRoles)
            && realmRoles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in realmRoles.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                AddRoleClaim(identity, role!);
            }
        }

        if (!string.IsNullOrWhiteSpace(clientId)
            && root.TryGetProperty("resource_access", out var resourceAccess)
            && resourceAccess.TryGetProperty(clientId, out var client)
            && client.TryGetProperty("roles", out var clientRoles)
            && clientRoles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in clientRoles.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                AddRoleClaim(identity, role!);
            }
        }
    }
    catch (FormatException ex)
    {
        ExceptionlessClient.Default.SubmitException(ex);
    }
    catch (JsonException ex)
    {
        ExceptionlessClient.Default.SubmitException(ex);
    }
}

static void AddRoleClaim(ClaimsIdentity identity, string role)
{
    if (string.IsNullOrWhiteSpace(role))
    {
        return;
    }

    if (!identity.HasClaim("roles", role))
    {
        identity.AddClaim(new Claim("roles", role));
    }

    if (!identity.HasClaim(ClaimTypes.Role, role))
    {
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }
}

static void AddGroupClaimsAsRoles(ClaimsIdentity identity, string groupClaimType)
{
    var groupClaims = identity.FindAll(groupClaimType).Select(c => c.Value).ToList();
    if (groupClaims.Count == 0)
    {
        return;
    }

    foreach (var value in groupClaims)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        if (value.TrimStart().StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var groupName in doc.RootElement.EnumerateArray().Select(x => x.GetString()))
                {
                    foreach (var candidate in GetGroupRoleCandidates(groupName))
                    {
                        AddRoleClaim(identity, candidate);
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed group claim payloads.
            }

            continue;
        }

        foreach (var candidate in GetGroupRoleCandidates(value))
        {
            AddRoleClaim(identity, candidate);
        }
    }
}

static IEnumerable<string> GetGroupRoleCandidates(string? rawGroup)
{
    if (string.IsNullOrWhiteSpace(rawGroup))
    {
        yield break;
    }

    var normalized = rawGroup.Trim().Trim('/');
    if (string.IsNullOrWhiteSpace(normalized))
    {
        yield break;
    }

    yield return normalized;

    var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var segment in segments)
    {
        yield return segment;
    }
}

static void ConfigureGlobalExceptionForwarding(ExceptionlessClient exceptionlessClient, ILogger startupLogger)
{
    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
        if (args.ExceptionObject is Exception ex)
        {
            exceptionlessClient.SubmitException(ex);
            StartupLog.LogUnhandledAppDomainExceptionCaptured(startupLogger, ex, args.IsTerminating);
            return;
        }

        StartupLog.LogUnhandledAppDomainExceptionObjectCaptured(startupLogger, args.IsTerminating);
    };

    TaskScheduler.UnobservedTaskException += (_, args) =>
    {
        exceptionlessClient.SubmitException(args.Exception);
        StartupLog.LogUnobservedTaskExceptionCaptured(startupLogger, args.Exception);
        args.SetObserved();
    };
}
