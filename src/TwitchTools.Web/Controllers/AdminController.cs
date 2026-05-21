using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminController(AppDbContext dbContext, ITwitchEventSubService twitchEventSubService) : Controller
{
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
        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(result);
    }

    [HttpPost("/admin/eventsub/resync")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EventSubForceResync(CancellationToken cancellationToken)
    {
        await twitchEventSubService.ForceResyncSubscriptionsAsync(cancellationToken);
        TempData["StatusMessage"] = "Force resync completed — all subscriptions deleted and re-bootstrapped.";
        return RedirectToAction(nameof(EventSub));
    }
}