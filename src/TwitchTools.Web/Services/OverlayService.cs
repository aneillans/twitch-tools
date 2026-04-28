using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public sealed class OverlayService(AppDbContext dbContext) : IOverlayService
{
    public async Task<OverlayViewModel?> GetByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.OverlayToken == overlayToken)
            .Select(x => new
            {
                x.OverlaySnapshot!.LastFollowerName,
                x.OverlaySnapshot.LastFollowerUtc,
                x.OverlaySnapshot.LastSubscriberName,
                x.OverlaySnapshot.LastSubscriberUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new OverlayViewModel
        {
            LastFollowerName = result.LastFollowerName,
            LastFollowerUtc = result.LastFollowerUtc,
            LastSubscriberName = result.LastSubscriberName,
            LastSubscriberUtc = result.LastSubscriberUtc
        };
    }
}