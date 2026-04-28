using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class TimedMessagesController(AppDbContext dbContext) : Controller
{
    [HttpGet("/my-tools/timed-messages")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            TempData["StatusMessage"] = "Save Twitch profile settings first before adding timed messages.";
            return RedirectToAction("Index", "MyTools");
        }

        var timedMessages = await dbContext.TimedChatMessages
            .AsNoTracking()
            .Where(x => x.StreamerId == streamer.Id)
            .OrderBy(x => x.Id)
            .Select(x => new TimedMessageItem
            {
                Id = x.Id,
                MessageText = x.MessageText,
                IntervalMinutes = (int)Math.Max(1, x.Interval.TotalMinutes),
                Enabled = x.Enabled,
                LastSentUtc = x.LastSentUtc
            })
            .ToListAsync(cancellationToken);

        var model = new TimedMessagesPageViewModel
        {
            TimedMessages = timedMessages
        };

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpPost("/my-tools/timed-messages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddTimedMessageInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.MessageText) || input.IntervalMinutes < 1)
        {
            TempData["StatusMessage"] = "Timed message text and interval are required.";
            return RedirectToAction(nameof(Index));
        }

        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            TempData["StatusMessage"] = "Save Twitch profile settings first before adding timed messages.";
            return RedirectToAction("Index", "MyTools");
        }

        dbContext.TimedChatMessages.Add(new TimedChatMessage
        {
            StreamerId = streamer.Id,
            MessageText = input.MessageText.Trim(),
            Interval = TimeSpan.FromMinutes(input.IntervalMinutes),
            Enabled = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Timed message added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/my-tools/timed-messages/{id:long}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(long id, CancellationToken cancellationToken)
    {
        var message = await dbContext.TimedChatMessages
            .Include(x => x.Streamer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (message is null || !IsOwner(message.Streamer.OwnerSubject))
        {
            TempData["StatusMessage"] = "Timed message was not found.";
            return RedirectToAction(nameof(Index));
        }

        message.Enabled = !message.Enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = message.Enabled ? "Timed message enabled." : "Timed message disabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/my-tools/timed-messages/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var message = await dbContext.TimedChatMessages
            .Include(x => x.Streamer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (message is null || !IsOwner(message.Streamer.OwnerSubject))
        {
            TempData["StatusMessage"] = "Timed message was not found.";
            return RedirectToAction(nameof(Index));
        }

        dbContext.TimedChatMessages.Remove(message);
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Timed message deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Streamer?> GetOwnedStreamerAsync(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (ownerSubject is null)
        {
            return null;
        }

        return await dbContext.Streamers.FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
    }

    private string? GetOwnerSubject()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }

    private bool IsOwner(string ownerSubject)
    {
        var current = GetOwnerSubject();
        return !string.IsNullOrWhiteSpace(current) && string.Equals(current, ownerSubject, StringComparison.Ordinal);
    }
}
