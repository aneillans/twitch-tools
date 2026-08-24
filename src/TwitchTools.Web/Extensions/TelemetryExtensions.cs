using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TwitchTools.Web.Extensions;

public static class TelemetryExtensions
{
    /// <summary>
    /// Wires OpenTelemetry traces + metrics + logs with OTLP export (endpoint from
    /// OTEL_EXPORTER_OTLP_ENDPOINT). Cloud-agnostic — points at any OTLP collector/backend,
    /// including the OpenSearch Data Prepper pipeline already present in the hosting environment.
    /// </summary>
    public static IServiceCollection AddTwitchToolsTelemetry(this IServiceCollection services, string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()   // GC/heap/threads/exceptions
                .AddOtlpExporter())
            .WithLogging(
                l => l.AddOtlpExporter(),
                // Emit the rendered message + scopes so log bodies are readable in OpenSearch,
                // not just structured state.
                o =>
                {
                    o.IncludeFormattedMessage = true;
                    // IncludeScopes stays OFF: ASP.NET Core emits the same key across nested scopes
                    // (e.g. HttpMethod/ConnectionId from the Kestrel + hosting scopes), which the OTLP
                    // exporter sends as duplicate log attributes — Data Prepper/OpenSearch then reject
                    // the whole record ("Duplicate key log.attributes.HttpMethod"). Trace↔log
                    // correlation is unaffected (it comes from the span context, not scopes).
                    o.IncludeScopes = false;
                });
        return services;
    }
}
