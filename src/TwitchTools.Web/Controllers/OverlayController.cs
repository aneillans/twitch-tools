using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
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

        SetStaticWidgetSecurityHeaders();
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

        SetStaticWidgetSecurityHeaders();
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

        var scriptNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        ViewData["ScriptNonce"] = scriptNonce;

        // Scripts only run from the nonced blocks and the CDNs the page loads, so viewer
        // chat inserted as HTML (inline handlers, injected <script>) cannot execute.
        // "sandbox allow-scripts" gives the page an opaque origin: it cannot read or send
        // the dashboard login cookie or read responses from the rest of the site.
        Response.Headers.ContentSecurityPolicy =
            "sandbox allow-scripts; " +
            "default-src 'self'; " +
            $"script-src 'nonce-{scriptNonce}' https://ajax.googleapis.com https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https:; " +
            "img-src 'self' https: data: blob:; " +
            "media-src 'self' https: data: blob:; " +
            "font-src 'self' https: data:; " +
            "connect-src 'self'; " +
            "frame-src 'none'; " +
            "object-src 'none'; " +
            "base-uri 'none'; " +
            "form-action 'none'";
        SetCommonSecurityHeaders();

        return View("CustomWidget", model);
    }

    [HttpGet("widgets/{token}/events")]
    public async Task Events(string token, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        // The widget page is sandboxed (opaque origin), so its EventSource request is cross-origin.
        // The token is the only credential here; no cookies are involved.
        Response.Headers.AccessControlAllowOrigin = "*";

        await Response.Body.FlushAsync(cancellationToken);

        await foreach (var payload in overlayEventBroker.SubscribeAsync(token, cancellationToken))
        {
            var formatted = $"data: {payload}\n\n";
            var bytes = Encoding.UTF8.GetBytes(formatted);
            await Response.Body.WriteAsync(bytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private void SetStaticWidgetSecurityHeaders()
    {
        // "allow-scripts" is needed for OBS Custom CSS (injected by script after load) and the
        // meta refresh; Chromium blocks both in a script-less sandbox. The page's own scripts
        // stay blocked by script-src 'none'. https: sources allow fonts and images in Custom CSS.
        Response.Headers.ContentSecurityPolicy =
            "sandbox allow-scripts; " +
            "default-src 'none'; " +
            "script-src 'none'; " +
            "style-src 'unsafe-inline' https:; " +
            "font-src https: data:; " +
            "img-src https: data:; " +
            "base-uri 'none'; " +
            "form-action 'none'";
        SetCommonSecurityHeaders();
    }

    private void SetCommonSecurityHeaders()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
