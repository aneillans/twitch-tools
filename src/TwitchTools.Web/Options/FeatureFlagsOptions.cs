namespace TwitchTools.Web.Options;

public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    public bool DisableExternalPosting { get; set; }
}