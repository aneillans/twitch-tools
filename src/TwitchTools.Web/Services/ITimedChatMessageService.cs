namespace TwitchTools.Web.Services;

public interface ITimedChatMessageService
{
    Task DispatchDueMessagesAsync(CancellationToken cancellationToken);
}