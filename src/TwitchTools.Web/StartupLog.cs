using Microsoft.Extensions.Logging;

namespace TwitchTools.Web;

internal static partial class StartupLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Exceptionless ServerUrl: {ServerUrl}")]
    public static partial void LogExceptionlessServerUrl(ILogger logger, string? serverUrl);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Exceptionless ApiKey configured: {HasKey}")]
    public static partial void LogExceptionlessApiKeyConfigured(ILogger logger, bool hasKey);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Exceptionless Enabled: {Enabled}")]
    public static partial void LogExceptionlessEnabled(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Exceptionless Storage implementation: {StorageType}")]
    public static partial void LogExceptionlessStorageImplementation(ILogger logger, string? storageType);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Exceptionless local storage path: {Path}")]
    public static partial void LogExceptionlessLocalStoragePath(ILogger logger, string path);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Configured Twitch EventSub callback URL: {EventSubCallbackUrl}")]
    public static partial void LogConfiguredTwitchEventSubCallbackUrl(ILogger logger, string? eventSubCallbackUrl);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Feature flag EnableEventSubIngressLogging: {Enabled}")]
    public static partial void LogFeatureFlagEnableEventSubIngressLogging(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Exceptionless local storage directory does not exist after initialization: {Path}")]
    public static partial void LogExceptionlessLocalStorageDirectoryMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "EventSub ingress candidate request received. Method={Method}, Path={Path}, Scheme={Scheme}, Host={Host}, X-Forwarded-Proto={ForwardedProto}, X-Forwarded-Host={ForwardedHost}")]
    public static partial void LogEventSubIngressCandidateReceived(ILogger logger, string method, string? path, string scheme, string? host, string forwardedProto, string forwardedHost);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "EventSub ingress candidate request completed. Method={Method}, Path={Path}, StatusCode={StatusCode}")]
    public static partial void LogEventSubIngressCandidateCompleted(ILogger logger, string method, string? path, int statusCode);

    [LoggerMessage(EventId = 11, Level = LogLevel.Critical, Message = "Unhandled AppDomain exception captured. IsTerminating={IsTerminating}")]
    public static partial void LogUnhandledAppDomainExceptionCaptured(ILogger logger, Exception exception, bool isTerminating);

    [LoggerMessage(EventId = 12, Level = LogLevel.Critical, Message = "Unhandled AppDomain exception object captured, but it was not an Exception instance. IsTerminating={IsTerminating}")]
    public static partial void LogUnhandledAppDomainExceptionObjectCaptured(ILogger logger, bool isTerminating);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Unobserved task exception captured.")]
    public static partial void LogUnobservedTaskExceptionCaptured(ILogger logger, Exception exception);
}