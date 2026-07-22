using Microsoft.Extensions.Logging;
#if ANDROID
using Android.Util;
#endif

namespace SistecreditoTEF.Maui.Common;

public static class AppLogger
{
    public static ILogger? Logger { get; private set; }

    public static void Init(ILogger logger)
    {
        Logger = logger;
    }

    public static void D(string tag, string message)
    {
#if ANDROID
        Log.Debug(tag, message);
#endif
        Logger?.LogDebug("[{Tag}] {Message}", tag, message);
    }

    public static void I(string tag, string message)
    {
#if ANDROID
        Log.Info(tag, message);
#endif
        Logger?.LogInformation("[{Tag}] {Message}", tag, message);
    }

    public static void W(string tag, string message, Exception? ex = null)
    {
#if ANDROID
        Log.Warn(tag, $"{message}{(ex is null ? "" : "\n" + ex)}");
#endif
        Logger?.LogWarning(ex, "[{Tag}] {Message}", tag, message);
    }

    public static void E(string tag, string message, Exception? ex = null)
    {
#if ANDROID
        Log.Error(tag, $"{message}{(ex is null ? "" : "\n" + ex)}");
#endif
        Logger?.LogError(ex, "[{Tag}] {Message}", tag, message);
    }
}
