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
}

public sealed record EventSubWebhookResult(int StatusCode, string? Body = null, string? ContentType = null);