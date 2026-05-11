using TwitchTools.Web.Domain;
using TwitchTools.Web.Services.Clients;

namespace TwitchTools.Web.Services;

public interface IBlueSkyService
{
    Task<string?> PublishLiveStateAsync(Streamer streamer, bool isLive, TwitchStreamStatus streamStatus, CancellationToken cancellationToken);
}