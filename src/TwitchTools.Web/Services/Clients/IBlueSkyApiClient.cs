namespace TwitchTools.Web.Services.Clients;

public interface IBlueSkyApiClient
{
    Task<string?> PublishLiveStatePostAsync(string streamerName, bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken);
    Task UpdateProfileLiveIndicatorAsync(bool isLive, BlueSkyCredentials credentials, CancellationToken cancellationToken);
    Task<bool> TestConnectionAsync(BlueSkyCredentials credentials, CancellationToken cancellationToken);
}

public sealed record BlueSkyCredentials(string Identifier, string AppPassword);