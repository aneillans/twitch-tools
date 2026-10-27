namespace TwitchTools.Web.Options;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// The collector's OTLP/HTTP traces URL (e.g. http://otel-collector:4318/v1/traces) that
    /// /otlp/v1/traces forwards browser spans to. Browser telemetry is off when empty.
    /// </summary>
    public string BrowserTracesEndpoint { get; set; } = string.Empty;
}
