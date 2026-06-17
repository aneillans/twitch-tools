using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[AllowAnonymous]
[Route("eventsub/twitch")]
public sealed class EventSubController(
    ITwitchEventSubService twitchEventSubService,
    ILogger<EventSubController> logger) : Controller
{
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var messageType = Request.Headers["Twitch-Eventsub-Message-Type"].ToString();
        var messageId = Request.Headers["Twitch-Eventsub-Message-Id"].ToString();
        var messageTimestamp = Request.Headers["Twitch-Eventsub-Message-Timestamp"].ToString();
        var messageSignature = Request.Headers["Twitch-Eventsub-Message-Signature"].ToString();

        logger.LogInformation(
            "Received Twitch EventSub callback. Path={Path}, MessageType={MessageType}, MessageId={MessageId}, HasSignature={HasSignature}, ContentLength={ContentLength}",
            Request.Path.Value,
            messageType,
            messageId,
            !string.IsNullOrWhiteSpace(messageSignature),
            rawBody.Length);

        var result = await twitchEventSubService.HandleWebhookAsync(
            messageType,
            messageId,
            messageTimestamp,
            messageSignature,
            rawBody,
            cancellationToken);

        logger.LogInformation(
            "Processed Twitch EventSub callback. Path={Path}, MessageType={MessageType}, MessageId={MessageId}, StatusCode={StatusCode}",
            Request.Path.Value,
            messageType,
            messageId,
            result.StatusCode);

        if (!string.IsNullOrWhiteSpace(result.Body))
        {
            return Content(result.Body, result.ContentType ?? "text/plain");
        }

        return StatusCode(result.StatusCode);
    }
}
