using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;

namespace TwitchTools.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminController(AppDbContext dbContext) : Controller
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
}