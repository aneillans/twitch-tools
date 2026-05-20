using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[AllowAnonymous]
[Route("overlay")]
public sealed class OverlayController(IOverlayService overlayService, IOverlayEventBroker overlayEventBroker) : Controller
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

    [HttpGet("widgets/{token}")]
    public async Task<IActionResult> Widget(string token, CancellationToken cancellationToken)
    {
        var model = await overlayService.GetCustomWidgetByTokenAsync(token, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("CustomWidget", model);
    }

    [HttpGet("widgets/{token}/events")]
    public async Task Events(string token, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        await Response.Body.FlushAsync(cancellationToken);

        await foreach (var payload in overlayEventBroker.SubscribeAsync(token, cancellationToken))
        {
            var formatted = $"data: {payload}\n\n";
            var bytes = Encoding.UTF8.GetBytes(formatted);
            await Response.Body.WriteAsync(bytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}