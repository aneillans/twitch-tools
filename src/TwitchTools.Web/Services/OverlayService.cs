using Microsoft.EntityFrameworkCore;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public sealed class OverlayService(AppDbContext dbContext) : IOverlayService
{
    public async Task<OverlayWidgetViewModel?> GetFollowerByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.FollowerOverlayToken == overlayToken)
            .Select(x => new
            {
                x.OverlaySnapshot!.LastFollowerName,
                x.OverlaySnapshot.LastFollowerUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new OverlayWidgetViewModel
        {
            Title = "Last Follower",
            EmptyMessage = "No recent follower",
            DisplayValue = result.LastFollowerName,
            EventUtc = result.LastFollowerUtc
        };
    }

    public async Task<OverlayWidgetViewModel?> GetSubscriberByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.SubscriberOverlayToken == overlayToken)
            .Select(x => new
            {
                x.OverlaySnapshot!.LastSubscriberName,
                x.OverlaySnapshot.LastSubscriberUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new OverlayWidgetViewModel
        {
            Title = "Last Subscriber",
            EmptyMessage = "No recent subscriber",
            DisplayValue = result.LastSubscriberName,
            EventUtc = result.LastSubscriberUtc
        };
    }
}