using Exceptionless;
using TwitchTools.Web.Services;

namespace TwitchTools.Web.Background;

public sealed class TimedChatMessageBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TimedChatMessageBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var timedChatService = scope.ServiceProvider.GetRequiredService<ITimedChatMessageService>();
                await timedChatService.DispatchDueMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Timed message dispatch cycle failed.");
                ExceptionlessClient.Default.SubmitException(ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}