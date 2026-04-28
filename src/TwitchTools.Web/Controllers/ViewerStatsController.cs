using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class ViewerStatsController(AppDbContext dbContext) : Controller
{
    private const int PageSize = 100;

    [HttpGet("/viewer-stats")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        var ownerSubject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Challenge();
        }

        var streamer = await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

        if (streamer is null)
        {
            ViewData["NoProfile"] = true;
            return View(new ViewerStatsViewModel());
        }

        // Use the max (latest cumulative) total per viewer to get aggregate watch time.
        var totalViewers = await dbContext.ViewerDurationSamples
            .Where(x => x.StreamerId == streamer.Id)
            .Select(x => x.TwitchViewerId)
            .Distinct()
            .CountAsync(cancellationToken);

        var currentPage = Math.Max(1, page);
        var skip = (currentPage - 1) * PageSize;

        var rows = await dbContext.ViewerDurationSamples
            .Where(x => x.StreamerId == streamer.Id)
            .GroupBy(x => x.TwitchViewerId)
            .Select(g => new ViewerStatRow
            {
                TwitchViewerId = g.Key,
                TotalSecondsWatched = g.Max(x => x.TotalSecondsWatched)
            })
            .OrderByDescending(x => x.TotalSecondsWatched)
            .Skip(skip)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        ViewData["CurrentPage"] = currentPage;
        ViewData["TotalPages"] = (int)Math.Ceiling(totalViewers / (double)PageSize);

        return View(new ViewerStatsViewModel
        {
            Rows = rows,
            TotalViewers = totalViewers
        });
    }
}
