using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class OidcDebugController : Controller
{
    [HttpGet("/oidc-debug")]
    public async Task<IActionResult> Index()
    {
        var principal = User;
        var identity = principal.Identity as ClaimsIdentity;

        var mappedRolesClaims = principal
            .FindAll("roles")
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mappedClaimTypeRoleClaims = principal
            .FindAll(ClaimTypes.Role)
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        var tokenRoles = ParseTokenRoles(accessToken);

        var model = new OidcDebugViewModel
        {
            UserName = principal.Identity?.Name ?? "(no name claim)",
            IsAuthenticated = principal.Identity?.IsAuthenticated == true,
            IsAdmin = principal.IsInRole("admin"),
            AuthenticationType = principal.Identity?.AuthenticationType ?? string.Empty,
            RoleClaimType = identity?.RoleClaimType,
            MappedRolesClaims = mappedRolesClaims,
            MappedClaimTypeRoleClaims = mappedClaimTypeRoleClaims,
            DetectedRealmRoles = tokenRoles.RealmRoles,
            DetectedResourceRoles = tokenRoles.ResourceRoles,
            AccessTokenRoleParseError = tokenRoles.Error,
            Claims = principal.Claims
                .OrderBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Value, StringComparer.Ordinal)
                .Select(c => new OidcDebugClaimViewModel
                {
                    Type = c.Type,
                    Value = c.Value
                })
                .ToArray()
        };

        return View(model);
    }

    private static ParsedTokenRoles ParseTokenRoles(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return ParsedTokenRoles.Empty;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return ParsedTokenRoles.WithError("Access token was not in JWT format.");
        }

        try
        {
            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

            var payloadBytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            var realmRoles = new List<string>();
            if (root.TryGetProperty("realm_access", out var realmAccess)
                && realmAccess.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray().Select(x => x.GetString()))
                {
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        realmRoles.Add(role);
                    }
                }
            }

            var resourceRoles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("resource_access", out var resourceAccess)
                && resourceAccess.ValueKind == JsonValueKind.Object)
            {
                foreach (var client in resourceAccess.EnumerateObject())
                {
                    if (!client.Value.TryGetProperty("roles", out var clientRoles)
                        || clientRoles.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var rolesForClient = clientRoles
                        .EnumerateArray()
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    resourceRoles[client.Name] = rolesForClient;
                }
            }

            return new ParsedTokenRoles(
                realmRoles
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                resourceRoles,
                null);
        }
        catch (Exception ex)
        {
            return ParsedTokenRoles.WithError($"Unable to parse access token roles: {ex.Message}");
        }
    }

    private sealed record ParsedTokenRoles(
        IReadOnlyList<string> RealmRoles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceRoles,
        string? Error)
    {
        public static ParsedTokenRoles Empty { get; } = new(
            Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            null);

        public static ParsedTokenRoles WithError(string error) => new(
            Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            error);
    }
}
