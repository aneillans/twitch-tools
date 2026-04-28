using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[AllowAnonymous]
[Route("overlay")]
public sealed class OverlayController(IOverlayService overlayService) : Controller
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Index(string token, CancellationToken cancellationToken)
    {
        var model = await overlayService.GetByTokenAsync(token, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }
}