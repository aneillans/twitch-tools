using Exceptionless;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TwitchTools.Web.Background;
using TwitchTools.Web.Data;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Clients;
using TwitchTools.Web.Services.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));
builder.Services.Configure<TwitchOptions>(builder.Configuration.GetSection(TwitchOptions.SectionName));
builder.Services.Configure<BlueSkyOptions>(builder.Configuration.GetSection(BlueSkyOptions.SectionName));
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection(EncryptionOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;

    // Accept forwarded headers from container/proxy networks.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.AccessDeniedPath = "/account/access-denied";
    })
    .AddOpenIdConnect(options =>
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddScoped<IOverlayService, OverlayService>();

builder.Services.AddHostedService<LiveStatusBackgroundService>();
builder.Services.AddHostedService<TimedChatMessageBackgroundService>();
builder.Services.AddHostedService<TwitchTokenRefreshBackgroundService>();
builder.Services.AddHostedService<ViewerMonitoringBackgroundService>();

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
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/account/login", async context =>
{
    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
});

app.MapGet("/account/logout", async context =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
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
    catch
    {
        // Ignore malformed tokens here; normal OIDC validation still applies.
    }
}

static void AddRoleClaim(ClaimsIdentity identity, string role)
{
    if (!identity.HasClaim("roles", role))
    {
        identity.AddClaim(new Claim("roles", role));
    }
}
