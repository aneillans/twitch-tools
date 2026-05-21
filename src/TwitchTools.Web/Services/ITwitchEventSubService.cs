namespace TwitchTools.Web.Services;

public interface ITwitchEventSubService
{
    Task<EventSubWebhookResult> HandleWebhookAsync(
        string messageType,
        string messageId,
        string messageTimestamp,
        string messageSignature,
        string rawBody,
        CancellationToken cancellationToken);

    Task EnsureSubscriberSubscriptionsAsync(CancellationToken cancellationToken);
    Task<EventSubDiagnosticsResult> GetDiagnosticsAsync(CancellationToken cancellationToken);
    Task ForceResyncSubscriptionsAsync(CancellationToken cancellationToken);
}

public sealed record EventSubWebhookResult(int StatusCode, string? Body = null, string? ContentType = null);

public sealed record EventSubSubscriptionStatus(
    string Id,
    string Status,
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition,
    DateTime CreatedAt);

public sealed record EventSubDiagnosticsResult(
    bool IsConfigured,
    IReadOnlyList<EventSubSubscriptionStatus> Subscriptions,
    int TotalCost,
    int MaxTotalCost,
    string? ErrorMessage);