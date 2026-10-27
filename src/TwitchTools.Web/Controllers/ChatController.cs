using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Models;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class ChatController(
    AppDbContext dbContext,
    [FromKeyedServices(StreamerChatFeed.BrokerKey)] IOverlayEventBroker streamerChatBroker,
    IChatModerationService chatModerationService,
    ILogger<ChatController> logger) : Controller
{
    // Comment lines keep proxies from closing an idle stream between chat messages.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly byte[] Heartbeat = Encoding.UTF8.GetBytes(": keep-alive\n\n");

    [HttpGet("/my-tools/chat")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            TempData["StatusMessage"] = "Save Twitch profile settings first before opening chat.";
            return RedirectToAction("Index", "MyTools");
        }

        return View(new ChatPageViewModel
        {
            TwitchConnected = !string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken),
            YouTubeConnected = !string.IsNullOrWhiteSpace(streamer.YouTubeStreamerAccessToken)
        });
    }

    [HttpGet("/my-tools/chat/events")]
    public async Task Events(CancellationToken cancellationToken)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");
        await Response.Body.FlushAsync(cancellationToken);

        // Last-Event-ID is sent by the browser when EventSource reconnects by itself.
        var lastEventId = Request.Headers["Last-Event-ID"].ToString();
        logger.LogInformation(
            "Chat stream opened for streamer {StreamerId}. LastEventId={LastEventId}",
            streamer.Id,
            lastEventId);

        var started = Stopwatch.GetTimestamp();
        long sent = 0;
        long? firstSequence = null;
        long? lastSequence = null;
        var reason = "client-disconnected";

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = streamerChatBroker.SubscribeAsync(StreamerChatFeed.Key(streamer.Id), stop.Token).GetAsyncEnumerator(stop.Token);
        var next = events.MoveNextAsync().AsTask();
        var heartbeat = Task.Delay(HeartbeatInterval, stop.Token);
        try
        {
            while (true)
            {
                if (await Task.WhenAny(next, heartbeat) != next)
                {
                    heartbeat = Task.Delay(HeartbeatInterval, stop.Token);
                    await Response.Body.WriteAsync(Heartbeat, stop.Token);
                    await Response.Body.FlushAsync(stop.Token);
                    continue;
                }

                if (!await next)
                {
                    reason = "broker-completed";
                    break;
                }

                var message = events.Current;
                var bytes = Encoding.UTF8.GetBytes($"id: {message.Sequence}\ndata: {message.Payload}\n\n");
                await Response.Body.WriteAsync(bytes, stop.Token);
                await Response.Body.FlushAsync(stop.Token);

                firstSequence ??= message.Sequence;
                lastSequence = message.Sequence;
                sent++;
                next = events.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser closed the stream.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            reason = "write-failed";
            logger.LogWarning(ex, "Chat stream write failed for streamer {StreamerId}.", streamer.Id);
        }
        finally
        {
            // A pending MoveNextAsync has to finish before the enumerator can be disposed.
            await stop.CancelAsync();
            try
            {
                await next;
            }
            catch (OperationCanceledException)
            {
            }

            await events.DisposeAsync();

            logger.LogInformation(
                "Chat stream closed for streamer {StreamerId}. Reason={Reason}, DurationSeconds={DurationSeconds}, MessagesSent={MessagesSent}, FirstSequence={FirstSequence}, LastSequence={LastSequence}",
                streamer.Id,
                reason,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 1),
                sent,
                firstSequence,
                lastSequence);
        }
    }

    [HttpPost("/my-tools/chat/moderate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Moderate([FromForm] ChatModerationInput input, CancellationToken cancellationToken)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            return NotFound();
        }

        var result = await chatModerationService.ModerateAsync(
            streamer,
            new ChatModerationRequest(input.Platform, input.Action, input.MessageId, input.UserId, input.DurationSeconds),
            cancellationToken);

        return Json(new { success = result.IsSuccess, message = result.Message });
    }

    private async Task<Streamer?> GetOwnedStreamerAsync(CancellationToken cancellationToken)
    {
        var ownerSubject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (ownerSubject is null)
        {
            return null;
        }

        return await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
    }
}
