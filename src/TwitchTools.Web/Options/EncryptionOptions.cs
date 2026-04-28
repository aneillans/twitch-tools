namespace TwitchTools.Web.Options;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public string Salt { get; set; } = string.Empty;
}
