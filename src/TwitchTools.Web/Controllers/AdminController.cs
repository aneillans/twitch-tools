using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Models;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminController(AppDbContext dbContext, ITwitchEventSubService twitchEventSubService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var recentLiveEvents = await dbContext.LiveNotificationEvents
            .OrderByDescending(x => x.RecordedUtc)
            .Take(20)
            .Select(x => new
            {
                x.StreamerId,
                x.IsLive,
                x.RecordedUtc,
                x.BlueSkyPostUri
            })
            .ToListAsync(cancellationToken);

        return View(recentLiveEvents);
    }

    [HttpGet("/admin/eventsub")]
    public async Task<IActionResult> EventSub(CancellationToken cancellationToken)
    {
        var result = await twitchEventSubService.GetDiagnosticsAsync(cancellationToken);
        var recentPayloads = await dbContext.EventSubDebugMessages
            .AsNoTracking()
            .Include(x => x.Streamer)
            .OrderByDescending(x => x.RecordedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var model = new AdminEventSubViewModel
        {
            Diagnostics = result,
            RecentPayloads = recentPayloads
        };

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpPost("/admin/eventsub/resync")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EventSubForceResync(CancellationToken cancellationToken)
    {
        var result = await twitchEventSubService.ForceResyncSubscriptionsAsync(cancellationToken);
        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(result.Message)
            ? $"Force resync completed. Deleted {result.DeletedCount}, created {result.EnsuredCount}, already existed {result.AlreadyExistsCount}, failed {result.FailedCount}."
            : $"Force resync completed with warnings. Deleted {result.DeletedCount}, created {result.EnsuredCount}, already existed {result.AlreadyExistsCount}, failed {result.FailedCount}. {result.Message}";
        return RedirectToAction(nameof(EventSub));
    }

    [HttpPost("/admin/eventsub/delete-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EventSubDeleteAll(CancellationToken cancellationToken)
    {
        var result = await twitchEventSubService.DeleteAllSubscriptionsAsync(cancellationToken);
        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(result.Message)
            ? $"Delete all completed. Deleted {result.DeletedCount}, failed {result.FailedCount}."
            : $"Delete all completed with warnings. Deleted {result.DeletedCount}, failed {result.FailedCount}. {result.Message}";
        return RedirectToAction(nameof(EventSub));
    }

    [HttpGet("/admin/known-bots")]
    public async Task<IActionResult> KnownBots(CancellationToken cancellationToken)
    {
        var knownBots = await dbContext.KnownBots
            .AsNoTracking()
            .OrderBy(x => x.Login ?? x.TwitchUserId)
            .Select(x => new KnownBotItem
            {
                Id = x.Id,
                TwitchUserId = x.TwitchUserId,
                Login = x.Login,
                Notes = x.Notes,
                CreatedUtc = x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var model = new AdminKnownBotsViewModel
        {
            KnownBots = knownBots
        };

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpPost("/admin/known-bots")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KnownBotsAdd(AddKnownBotInput input, CancellationToken cancellationToken)
    {
        var twitchUserId = input.TwitchUserId?.Trim();
        if (string.IsNullOrWhiteSpace(twitchUserId))
        {
            TempData["StatusMessage"] = "A Twitch user ID is required to add a known bot.";
            return RedirectToAction(nameof(KnownBots));
        }

        var alreadyExists = await dbContext.KnownBots
            .AnyAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (alreadyExists)
        {
            TempData["StatusMessage"] = "That Twitch user ID is already in the known bot list.";
            return RedirectToAction(nameof(KnownBots));
        }

        dbContext.KnownBots.Add(new KnownBot
        {
            TwitchUserId = twitchUserId,
            Login = string.IsNullOrWhiteSpace(input.Login) ? null : input.Login.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Known bot added.";
        return RedirectToAction(nameof(KnownBots));
    }

    [HttpPost("/admin/known-bots/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KnownBotsDelete(int id, CancellationToken cancellationToken)
    {
        var knownBot = await dbContext.KnownBots.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (knownBot is null)
        {
            TempData["StatusMessage"] = "Known bot was not found.";
            return RedirectToAction(nameof(KnownBots));
        }

        dbContext.KnownBots.Remove(knownBot);
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Known bot removed.";
        return RedirectToAction(nameof(KnownBots));
    }
}