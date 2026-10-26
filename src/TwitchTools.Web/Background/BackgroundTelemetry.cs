using System.Diagnostics;

namespace TwitchTools.Web.Background;

/// <summary>
/// Spans for background work. Background loops run with no ambient <see cref="Activity"/>, so without
/// a span per cycle their logs carry no trace ID and every Twitch/YouTube call or database command
/// becomes its own single-span trace. One span per cycle groups all of that under one trace.
/// </summary>
internal static class BackgroundTelemetry
{
    public const string SourceName = "TwitchTools.Background";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Starts the span for one cycle. Dispose it before the loop's delay so the wait is not counted.
    /// </summary>
    public static Activity? StartCycle(string name) => Source.StartActivity(name, ActivityKind.Internal);

    /// <summary>
    /// Starts a span for queued work as a child of the trace that queued it, so the request that
    /// triggered the work and the work itself appear as one trace.
    /// </summary>
    public static Activity? StartQueuedWork(string name, ActivityContext origin) =>
        Source.StartActivity(name, ActivityKind.Internal, origin);

    public static void RecordFailure(this Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddException(ex);
    }
}
