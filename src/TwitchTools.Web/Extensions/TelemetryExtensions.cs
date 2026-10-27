using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TwitchTools.Web.Background;
using TwitchTools.Web.Services;

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
                // Browser span uploads (TelemetryController) would otherwise add a server span and
                // a collector call span for every batch the page sends.
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/otlp/v1/traces")
                .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage = req => req.RequestUri?.AbsolutePath != "/v1/traces")
                // One span per background cycle / queued item (Background/BackgroundTelemetry.cs).
                .AddSource(BackgroundTelemetry.SourceName)
                // Npgsql's built-in ActivitySource: a span per database command, nested under the
                // request or background cycle that ran it. This is all Npgsql.OpenTelemetry's
                // AddNpgsql() does, so no extra package.
                .AddSource("Npgsql")
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()   // GC/heap/threads/exceptions
                .AddMeter(OverlayEventBroker.MeterName)
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
