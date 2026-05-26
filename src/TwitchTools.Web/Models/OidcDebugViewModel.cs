namespace TwitchTools.Web.Models;

public sealed class OidcDebugViewModel
{
    public string UserName { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }
    public bool IsAdmin { get; init; }
    public string AuthenticationType { get; init; } = string.Empty;
    public string? RoleClaimType { get; init; }
    public IReadOnlyList<string> MappedRolesClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MappedClaimTypeRoleClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DetectedRealmRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DetectedResourceRoles { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<OidcDebugClaimViewModel> Claims { get; init; } = Array.Empty<OidcDebugClaimViewModel>();
    public string? AccessTokenRoleParseError { get; init; }
}

public sealed class OidcDebugClaimViewModel
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
