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
    IChatModerationService chatModerationService) : Controller
{
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

        await foreach (var payload in streamerChatBroker.SubscribeAsync(StreamerChatFeed.Key(streamer.Id), cancellationToken))
        {
            var bytes = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
            await Response.Body.WriteAsync(bytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
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
