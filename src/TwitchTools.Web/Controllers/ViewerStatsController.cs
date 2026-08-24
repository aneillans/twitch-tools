using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;
using TwitchTools.Web.Options;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class ViewerStatsController(
    AppDbContext dbContext,
    ITwitchApiClient twitchApiClient,
    Microsoft.Extensions.Options.IOptions<TwitchOptions> twitchOptions) : Controller
{
    private const int PageSize = 100;

    [HttpGet("/viewer-stats")]
    public async Task<IActionResult> Index(int page = 1, bool includeBots = false, CancellationToken cancellationToken = default)
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

        // Known bots are excluded by default; the viewer can opt back in via the "includeBots" toggle.
        var knownBotIds = includeBots
            ? []
            : await dbContext.KnownBots
                .AsNoTracking()
                .Select(x => x.TwitchUserId)
                .ToListAsync(cancellationToken);

        var baseQuery = dbContext.ViewerDurationSamples
            .Where(x => x.StreamerId == streamer.Id && !knownBotIds.Contains(x.TwitchViewerId));

        // Use the max (latest cumulative) total per viewer to get aggregate watch time.
        var totalViewers = await baseQuery
            .Select(x => x.TwitchViewerId)
            .Distinct()
            .CountAsync(cancellationToken);

        var currentPage = Math.Max(1, page);
        var skip = (currentPage - 1) * PageSize;

        var rows = await baseQuery
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

        await PopulateViewerNamesAsync(streamer, rows, cancellationToken);

        ViewData["CurrentPage"] = currentPage;
        ViewData["TotalPages"] = (int)Math.Ceiling(totalViewers / (double)PageSize);
        ViewData["IncludeBots"] = includeBots;

        return View(new ViewerStatsViewModel
        {
            Rows = rows,
            TotalViewers = totalViewers
        });
    }

    private async Task PopulateViewerNamesAsync(
        Domain.Streamer streamer,
        List<ViewerStatRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var auth = BuildUserLookupAuthContext(streamer);
        if (auth is null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i] = new ViewerStatRow
                {
                    TwitchViewerId = rows[i].TwitchViewerId,
                    TwitchUserName = rows[i].TwitchViewerId,
                    IsDeletedUser = false,
                    TotalSecondsWatched = rows[i].TotalSecondsWatched
                };
            }

            return;
        }

        var viewerIds = rows.Select(x => x.TwitchViewerId).ToArray();
        var usersById = await twitchApiClient.GetUsersByIdsAsync(viewerIds, auth, cancellationToken);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (usersById.TryGetValue(row.TwitchViewerId, out var user))
            {
                rows[i] = new ViewerStatRow
                {
                    TwitchViewerId = row.TwitchViewerId,
                    TwitchUserName = user.DisplayName ?? user.UserLogin ?? row.TwitchViewerId,
                    IsDeletedUser = false,
                    TotalSecondsWatched = row.TotalSecondsWatched
                };
                continue;
            }

            rows[i] = new ViewerStatRow
            {
                TwitchViewerId = row.TwitchViewerId,
                TwitchUserName = "Deleted user",
                IsDeletedUser = true,
                TotalSecondsWatched = row.TotalSecondsWatched
            };
        }
    }

    private TwitchAuthContext? BuildUserLookupAuthContext(Domain.Streamer streamer)
    {
        var clientId = string.IsNullOrWhiteSpace(streamer.TwitchClientId)
            ? twitchOptions.Value.DefaultClientId
            : streamer.TwitchClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(streamer.TwitchBotAccessToken))
        {
            return new TwitchAuthContext(
                clientId,
                streamer.TwitchBotAccessToken,
                streamer.TwitchBotUserId,
                streamer.TwitchBotUserId);
        }

        if (!string.IsNullOrWhiteSpace(streamer.TwitchStreamerAccessToken))
        {
            return new TwitchAuthContext(
                clientId,
                streamer.TwitchStreamerAccessToken,
                streamer.TwitchUserId,
                streamer.TwitchUserId);
        }

        return null;
    }
}
