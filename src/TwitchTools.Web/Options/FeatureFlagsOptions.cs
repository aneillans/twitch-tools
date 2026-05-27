namespace TwitchTools.Web.Options;

public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    public bool DisableExternalPosting { get; set; }
    public bool EnableOidcDebug { get; set; }
    public bool EnableEventSubIngressLogging { get; set; }
}