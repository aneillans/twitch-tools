using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

public interface IBlueSkyService
{
    Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, CancellationToken cancellationToken);
}