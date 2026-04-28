using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;

namespace TwitchTools.Web.Controllers;

[Authorize]
public sealed class DashboardController(AppDbContext dbContext) : Controller
{
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

        var streamerId = streamer?.Id;
        var model = new
        {
            HasProfile = streamer is not null,
            StreamerCount = streamer is null ? 0 : 1,
            TimedMessageCount = streamerId is null
                ? 0
                : await dbContext.TimedChatMessages.CountAsync(x => x.StreamerId == streamerId, cancellationToken),
            DiscordSyncCount = streamerId is null
                ? 0
                : await dbContext.DiscordGuildSyncs.CountAsync(x => x.StreamerId == streamerId, cancellationToken)
        };

        return View(model);
    }
}