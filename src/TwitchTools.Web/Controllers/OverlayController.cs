using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[AllowAnonymous]
[Route("overlay")]
public sealed class OverlayController(IOverlayService overlayService) : Controller
{
    [HttpGet("followers/{token}")]
    public async Task<IActionResult> Followers(string token, CancellationToken cancellationToken)
    {
        var model = await overlayService.GetFollowerByTokenAsync(token, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("Widget", model);
    }

    [HttpGet("subscribers/{token}")]
    public async Task<IActionResult> Subscribers(string token, CancellationToken cancellationToken)
    {
        var model = await overlayService.GetSubscriberByTokenAsync(token, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("Widget", model);
    }
}