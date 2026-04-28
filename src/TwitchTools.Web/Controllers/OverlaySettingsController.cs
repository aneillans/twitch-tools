using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class OverlaySettingsController(AppDbContext dbContext) : Controller
{
    [HttpGet("/my-tools/overlay")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
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
            TempData["StatusMessage"] = "Save Twitch profile settings first before using overlay URLs.";
            return RedirectToAction("Index", "MyTools");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var model = new OverlaySettingsViewModel
        {
            FollowerOverlayToken = streamer.FollowerOverlayToken,
            SubscriberOverlayToken = streamer.SubscriberOverlayToken,
            FollowerOverlayUrl = $"{baseUrl}/overlay/followers/{streamer.FollowerOverlayToken}",
            SubscriberOverlayUrl = $"{baseUrl}/overlay/subscribers/{streamer.SubscriberOverlayToken}"
        };

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }
}
