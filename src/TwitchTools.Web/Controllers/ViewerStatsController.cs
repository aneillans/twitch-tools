using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
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
    private const int MaxSuggestedLinks = 20;

    private sealed record TwitchNameInfo(string? Name, bool IsDeleted);

    private sealed record YouTubeViewerTotal(string ViewerId, int Seconds, string? DisplayName);

    [HttpGet("/viewer-stats")]
    public async Task<IActionResult> Index(
        int page = 1,
        bool includeBots = false,
        string platform = "all",
        CancellationToken cancellationToken = default)
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

        // Known bots are excluded by default; the viewer can opt back in via the "includeBots"
        // toggle. There is no YouTube known-bot list yet, so this only filters Twitch viewers.
        var knownBotIds = includeBots
            ? []
            : await dbContext.KnownBots
                .AsNoTracking()
                .Select(x => x.TwitchUserId)
                .ToListAsync(cancellationToken);

        var twitchTotals = await dbContext.ViewerDurationSamples
            .Where(x => x.StreamerId == streamer.Id && !knownBotIds.Contains(x.TwitchViewerId))
            .GroupBy(x => x.TwitchViewerId)
            .Select(g => new { ViewerId = g.Key, Seconds = g.Max(x => x.TotalSecondsWatched) })
            .ToDictionaryAsync(x => x.ViewerId, x => x.Seconds, cancellationToken);

        var youTubeSamples = await dbContext.YouTubeViewerDurationSamples
            .AsNoTracking()
            .Where(x => x.StreamerId == streamer.Id)
            .Select(x => new { x.YouTubeViewerId, x.YouTubeViewerDisplayName, x.TotalSecondsWatched, x.CapturedUtc })
            .ToListAsync(cancellationToken);

        var youTubeTotals = youTubeSamples
            .GroupBy(x => x.YouTubeViewerId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.CapturedUtc).First();
                return new YouTubeViewerTotal(g.Key, latest.TotalSecondsWatched, latest.YouTubeViewerDisplayName);
            })
            .ToList();
        var youTubeTotalsById = youTubeTotals.ToDictionary(x => x.ViewerId, StringComparer.Ordinal);

        var links = await dbContext.ViewerIdentityLinks
            .AsNoTracking()
            .Where(x => x.StreamerId == streamer.Id)
            .ToListAsync(cancellationToken);

        var linkedTwitchIds = links.Select(x => x.TwitchViewerId).ToHashSet(StringComparer.Ordinal);
        var linkedYouTubeIds = links.Select(x => x.YouTubeViewerId).ToHashSet(StringComparer.Ordinal);

        var twitchNamesById = await ResolveTwitchNamesAsync(streamer, twitchTotals.Keys, cancellationToken);

        var rows = new List<ViewerStatRow>();

        foreach (var link in links)
        {
            var hasTwitch = twitchTotals.TryGetValue(link.TwitchViewerId, out var twitchSeconds);
            var hasYouTube = youTubeTotalsById.TryGetValue(link.YouTubeViewerId, out var youTubeInfo);
            if (!hasTwitch && !hasYouTube)
            {
                // No remaining samples on either side for this link (e.g. bot-filtered out); skip.
                continue;
            }

            twitchNamesById.TryGetValue(link.TwitchViewerId, out var twitchNameInfo);

            rows.Add(new ViewerStatRow
            {
                LinkId = link.Id,
                TwitchViewerId = link.TwitchViewerId,
                TwitchUserName = twitchNameInfo?.Name,
                IsDeletedUser = twitchNameInfo?.IsDeleted ?? false,
                TwitchSecondsWatched = hasTwitch ? twitchSeconds : 0,
                YouTubeViewerId = link.YouTubeViewerId,
                YouTubeUserName = hasYouTube ? (youTubeInfo!.DisplayName ?? link.YouTubeViewerId) : null,
                YouTubeSecondsWatched = hasYouTube ? youTubeInfo!.Seconds : 0
            });
        }

        foreach (var (viewerId, seconds) in twitchTotals)
        {
            if (linkedTwitchIds.Contains(viewerId))
            {
                continue;
            }

            twitchNamesById.TryGetValue(viewerId, out var nameInfo);
            rows.Add(new ViewerStatRow
            {
                TwitchViewerId = viewerId,
                TwitchUserName = nameInfo?.Name,
                IsDeletedUser = nameInfo?.IsDeleted ?? false,
                TwitchSecondsWatched = seconds
            });
        }

        foreach (var info in youTubeTotals)
        {
            if (linkedYouTubeIds.Contains(info.ViewerId))
            {
                continue;
            }

            rows.Add(new ViewerStatRow
            {
                YouTubeViewerId = info.ViewerId,
                YouTubeUserName = info.DisplayName ?? info.ViewerId,
                YouTubeSecondsWatched = info.Seconds
            });
        }

        var filteredRows = platform.ToUpperInvariant() switch
        {
            "TWITCH" => rows.Where(x => x.TwitchViewerId is not null).ToList(),
            "YOUTUBE" => rows.Where(x => x.YouTubeViewerId is not null).ToList(),
            "LINKED" => rows.Where(x => x.IsLinked).ToList(),
            _ => rows
        };

        var totalViewers = filteredRows.Count;
        var currentPage = Math.Max(1, page);

        var pagedRows = filteredRows
            .OrderByDescending(x => x.TotalSecondsWatched)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        var suggestedLinks = BuildSuggestedLinks(
            twitchNamesById,
            twitchTotals.Keys.Where(id => !linkedTwitchIds.Contains(id)),
            youTubeTotals.Where(x => !linkedYouTubeIds.Contains(x.ViewerId)));

        ViewData["CurrentPage"] = currentPage;
        ViewData["TotalPages"] = (int)Math.Ceiling(totalViewers / (double)PageSize);
        ViewData["IncludeBots"] = includeBots;
        ViewData["Platform"] = platform;
        ViewData["StatusMessage"] = TempData["StatusMessage"] as string;

        return View(new ViewerStatsViewModel
        {
            Rows = pagedRows,
            TotalViewers = totalViewers,
            SuggestedLinks = suggestedLinks,
            UnlinkedTwitchViewers = twitchTotals.Keys
                .Where(id => !linkedTwitchIds.Contains(id))
                .Select(id => new ViewerPickerItem
                {
                    Id = id,
                    DisplayName = twitchNamesById.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n.Name) ? n.Name! : id
                })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UnlinkedYouTubeViewers = youTubeTotals
                .Where(x => !linkedYouTubeIds.Contains(x.ViewerId))
                .Select(x => new ViewerPickerItem { Id = x.ViewerId, DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.ViewerId : x.DisplayName })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        });
    }

    [HttpPost("/viewer-stats/link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(
        string twitchViewerId,
        string youTubeViewerId,
        int page = 1,
        bool includeBots = false,
        string platform = "all",
        CancellationToken cancellationToken = default)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(twitchViewerId) || string.IsNullOrWhiteSpace(youTubeViewerId))
        {
            TempData["StatusMessage"] = "Pick both a Twitch viewer and a YouTube viewer to link.";
            return RedirectToViewerStats(page, includeBots, platform);
        }

        var alreadyLinked = await dbContext.ViewerIdentityLinks
            .AnyAsync(
                x => x.StreamerId == streamer.Id && (x.TwitchViewerId == twitchViewerId || x.YouTubeViewerId == youTubeViewerId),
                cancellationToken);
        if (alreadyLinked)
        {
            TempData["StatusMessage"] = "One of those viewers is already linked to someone else. Unlink them first.";
            return RedirectToViewerStats(page, includeBots, platform);
        }

        dbContext.ViewerIdentityLinks.Add(new ViewerIdentityLink
        {
            StreamerId = streamer.Id,
            TwitchViewerId = twitchViewerId,
            YouTubeViewerId = youTubeViewerId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Viewers linked.";
        return RedirectToViewerStats(page, includeBots, platform);
    }

    [HttpPost("/viewer-stats/unlink/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(
        long id,
        int page = 1,
        bool includeBots = false,
        string platform = "all",
        CancellationToken cancellationToken = default)
    {
        var streamer = await GetOwnedStreamerAsync(cancellationToken);
        if (streamer is null)
        {
            return Challenge();
        }

        var link = await dbContext.ViewerIdentityLinks
            .FirstOrDefaultAsync(x => x.Id == id && x.StreamerId == streamer.Id, cancellationToken);
        if (link is not null)
        {
            dbContext.ViewerIdentityLinks.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = "Viewers unlinked.";
        }

        return RedirectToViewerStats(page, includeBots, platform);
    }

    private IActionResult RedirectToViewerStats(int page, bool includeBots, string platform)
    {
        return RedirectToAction(nameof(Index), new { page, includeBots, platform });
    }

    private async Task<Domain.Streamer?> GetOwnedStreamerAsync(CancellationToken cancellationToken)
    {
        var ownerSubject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return null;
        }

        return await dbContext.Streamers.FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
    }

    private static List<SuggestedLinkRow> BuildSuggestedLinks(
        IReadOnlyDictionary<string, TwitchNameInfo> twitchNamesById,
        IEnumerable<string> unlinkedTwitchIds,
        IEnumerable<YouTubeViewerTotal> unlinkedYouTubeViewers)
    {
        var youTubeByNormalizedName = unlinkedYouTubeViewers
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => NormalizeName(x.DisplayName!))
            .Where(g => g.Key.Length > 0 && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        var suggestions = new List<SuggestedLinkRow>();
        foreach (var twitchId in unlinkedTwitchIds)
        {
            if (!twitchNamesById.TryGetValue(twitchId, out var twitchName)
                || twitchName.IsDeleted
                || string.IsNullOrWhiteSpace(twitchName.Name))
            {
                continue;
            }

            var normalized = NormalizeName(twitchName.Name);
            if (normalized.Length == 0 || !youTubeByNormalizedName.TryGetValue(normalized, out var match))
            {
                continue;
            }

            suggestions.Add(new SuggestedLinkRow
            {
                TwitchViewerId = twitchId,
                TwitchUserName = twitchName.Name,
                YouTubeViewerId = match.ViewerId,
                YouTubeUserName = match.DisplayName ?? match.ViewerId
            });

            if (suggestions.Count >= MaxSuggestedLinks)
            {
                break;
            }
        }

        return suggestions;
    }

    private static string NormalizeName(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private async Task<Dictionary<string, TwitchNameInfo>> ResolveTwitchNamesAsync(
        Domain.Streamer streamer,
        IEnumerable<string> viewerIds,
        CancellationToken cancellationToken)
    {
        var ids = viewerIds.ToArray();
        var result = new Dictionary<string, TwitchNameInfo>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return result;
        }

        var auth = BuildUserLookupAuthContext(streamer);
        if (auth is null)
        {
            foreach (var id in ids)
            {
                result[id] = new TwitchNameInfo(id, false);
            }

            return result;
        }

        var usersById = await twitchApiClient.GetUsersByIdsAsync(ids, auth, cancellationToken);
        foreach (var id in ids)
        {
            result[id] = usersById.TryGetValue(id, out var user)
                ? new TwitchNameInfo(user.DisplayName ?? user.UserLogin ?? id, false)
                : new TwitchNameInfo("Deleted user", true);
        }

        return result;
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
