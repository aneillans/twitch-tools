using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class OverlaySettingsController(
    AppDbContext dbContext,
    IOverlayService overlayService,
    IOverlayEventBroker overlayEventBroker,
    ILogger<OverlaySettingsController> logger) : Controller
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
    [RequestFormLimits(ValueLengthLimit = 16 * 1024 * 1024, ValueCountLimit = 2048)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> SaveCustomWidget([Bind(Prefix = "CustomWidget")] CustomOverlayWidgetInput input, CancellationToken cancellationToken)
    {
        var ownerSubject = GetOwnerSubject();
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Challenge();
        }

        logger.LogInformation(
            "Custom widget save requested for owner {OwnerSubject}. Lengths html={HtmlLength}, css={CssLength}, js={JsLength}, fields={FieldsLength}, data={DataLength}",
            ownerSubject,
            input.Html?.Length ?? 0,
            input.Css?.Length ?? 0,
            input.Js?.Length ?? 0,
            input.FieldsJson?.Length ?? 0,
            input.DataJson?.Length ?? 0);

        if (!ModelState.IsValid)
        {
            var modelStateErrors = string.Join(" ",
                ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                        ? e.Exception?.Message
                        : e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));

            logger.LogWarning("Custom widget save rejected by model binding for owner {OwnerSubject}. Errors: {Errors}",
                ownerSubject,
                modelStateErrors);

            var streamerForInvalidModel = await dbContext.Streamers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

            if (streamerForInvalidModel is null)
            {
                TempData["StatusMessage"] = "Save Twitch profile settings first before using overlay URLs.";
                return RedirectToAction("Index", "MyTools");
            }

            ViewData["StatusMessage"] = string.IsNullOrWhiteSpace(modelStateErrors)
                ? "Unable to save custom widget. The posted form data could not be parsed."
                : $"Unable to save custom widget: {modelStateErrors}";

            var invalidModelView = BuildViewModel(streamerForInvalidModel);
            invalidModelView.CustomWidget = input;
            return View("Index", invalidModelView);
        }

        try
        {
            await overlayService.SaveCustomWidgetAsync(ownerSubject, input, cancellationToken);
            logger.LogInformation("Custom widget save completed for owner {OwnerSubject}", ownerSubject);
            TempData["StatusMessage"] = "Custom StreamElements widget saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Custom widget save validation failed for owner {OwnerSubject}", ownerSubject);
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected custom widget save error for owner {OwnerSubject}", ownerSubject);

            var streamer = await dbContext.Streamers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);

            if (streamer is null)
            {
                TempData["StatusMessage"] = "Save Twitch profile settings first before using overlay URLs.";
                return RedirectToAction("Index", "MyTools");
            }

            ViewData["StatusMessage"] = "Unable to save custom widget due to an unexpected error. Check logs for details.";
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
