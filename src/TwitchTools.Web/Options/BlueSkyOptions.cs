namespace TwitchTools.Web.Options;

public sealed class BlueSkyOptions
{
    public const string SectionName = "BlueSky";

    public string ServiceUrl { get; set; } = "https://bsky.social";
    public string LiveProfilePrefix { get; set; } = "LIVE: ";
}