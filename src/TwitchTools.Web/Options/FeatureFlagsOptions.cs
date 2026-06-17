namespace TwitchTools.Web.Options;

#pragma warning disable CA1515
public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    public bool DisableExternalPosting { get; set; }
    public bool EnableOidcDebug { get; set; }
    public bool EnableEventSubIngressLogging { get; set; }
}
#pragma warning restore CA1515