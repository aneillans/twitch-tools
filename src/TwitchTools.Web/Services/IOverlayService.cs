using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public interface IOverlayService
{
    Task<OverlayViewModel?> GetByTokenAsync(string overlayToken, CancellationToken cancellationToken);
}