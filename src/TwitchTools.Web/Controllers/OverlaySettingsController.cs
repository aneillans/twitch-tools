using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class OverlaySettingsController(
    AppDbContext dbContext,
    IOverlayService overlayService,
    IOverlayEventBroker overlayEventBroker) : Controller
{
    [HttpGet("/my-tools/overlay")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
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

        var model = BuildViewModel(streamer);

        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;
        return View(model);
    }

    [HttpPost("/my-tools/overlay/custom-widget")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCustomWidget(CustomOverlayWidgetInput input, CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Challenge();
        }

        try
        {
            await overlayService.SaveCustomWidgetAsync(ownerSubject, input, cancellationToken);
            TempData["StatusMessage"] = "Custom StreamElements widget saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            var streamer = await dbContext.Streamers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

            if (streamer is null)
            {
                TempData["StatusMessage"] = "Save Twitch profile settings first before using overlay URLs.";
                return RedirectToAction("Index", "MyTools");
            }

            ViewData["StatusMessage"] = ex.Message;
            var model = BuildViewModel(streamer);
            model.CustomWidget = input;
            return View("Index", model);
        }
    }

    [HttpPost("/my-tools/overlay/custom-widget/test-message")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCustomWidgetTestMessage(CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Challenge();
        }

        var streamer = await dbContext.Streamers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

        if (streamer is null || string.IsNullOrWhiteSpace(streamer.CustomOverlayToken))
        {
            TempData["StatusMessage"] = "Save a custom widget first to enable test events.";
            return RedirectToAction(nameof(Index));
        }

        var payload = new
        {
            listener = "message",
            @event = new
            {
                data = new
                {
                    text = "Hello from twitch-tools custom overlay test message.",
                    displayName = "TwitchTools",
                    nick = "twitchtools",
                    userId = "test-user",
                    msgId = $"msg-{Guid.NewGuid():N}",
                    badges = Array.Empty<object>(),
                    tags = new
                    {
                        badges = string.Empty,
                        subscriber = "0",
                        mod = "0",
                        color = "#5E88FC"
                    }
                },
                renderedText = "Hello from twitch-tools custom overlay test message."
            }
        };

        await overlayEventBroker.PublishAsync(
            streamer.CustomOverlayToken,
            JsonSerializer.Serialize(payload),
            cancellationToken);

        TempData["StatusMessage"] = "Test chat message sent to active custom overlay viewers.";
        return RedirectToAction(nameof(Index));
    }

    private string? GetOwnerSubject()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }

    private OverlaySettingsViewModel BuildViewModel(TwitchTools.Web.Domain.Streamer streamer)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        return new OverlaySettingsViewModel
        {
            FollowerOverlayToken = streamer.FollowerOverlayToken,
            SubscriberOverlayToken = streamer.SubscriberOverlayToken,
            FollowerOverlayUrl = $"{baseUrl}/overlay/followers/{streamer.FollowerOverlayToken}",
            SubscriberOverlayUrl = $"{baseUrl}/overlay/subscribers/{streamer.SubscriberOverlayToken}",
            CustomWidgetOverlayToken = streamer.CustomOverlayToken,
            CustomWidgetOverlayUrl = string.IsNullOrWhiteSpace(streamer.CustomOverlayToken)
                ? null
                : $"{baseUrl}/overlay/widgets/{streamer.CustomOverlayToken}",
            CustomWidgetUpdatedUtc = streamer.CustomOverlayUpdatedUtc,
            CustomWidget = new CustomOverlayWidgetInput
            {
                Name = string.IsNullOrWhiteSpace(streamer.CustomOverlayName)
                    ? "Imported StreamElements Widget"
                    : streamer.CustomOverlayName,
                Html = streamer.CustomOverlayHtml ?? string.Empty,
                Css = streamer.CustomOverlayCss ?? string.Empty,
                Js = streamer.CustomOverlayJs ?? string.Empty,
                FieldsJson = streamer.CustomOverlayFieldsJson ?? "{}",
                DataJson = streamer.CustomOverlayDataJson ?? "{}"
            }
        };
    }
}
