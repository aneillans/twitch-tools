using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
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
        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(result);
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
}