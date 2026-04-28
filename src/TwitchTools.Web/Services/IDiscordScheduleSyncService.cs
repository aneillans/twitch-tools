using TwitchTools.Web.Domain;

namespace TwitchTools.Web.Services;

public interface IDiscordScheduleSyncService
{
    Task SyncScheduleAsync(Streamer streamer, CancellationToken cancellationToken);
}